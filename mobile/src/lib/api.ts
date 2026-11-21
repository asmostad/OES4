import { Platform } from 'react-native';

import { storage } from './storage';

// ---------- Types: the server's JSON (camelCase), from src/Oes.Web/Controllers/Api ----------

export type Tokens = { accessToken: string; refreshToken: string; expiresIn: number };
export type TenantOption = { id: string; name: string };
export type Me = { userId: string; firstName: string; lastName: string; email: string; tenantId: string; tenantName: string; permissions: string[] };

/** Dates from the server's "local" fields are wall-clock times in the company's time zone, with no offset. */
export type TaskRow = {
  id: string; title: string; notes: string | null; dueLocal: string | null; hasDueTime: boolean; priority: string;
  assigneeName: string; completedAt: string | null; entityType: string | null; entityId: string | null;
  isOverdue: boolean; isDueToday: boolean; canEdit: boolean;
};
export type AppointmentRow = {
  id: string; title: string; typeKey: string; startLocal: string; endLocal: string; location: string | null; notes: string | null;
  assigneeName: string; status: string; entityType: string | null; entityId: string | null; isAllDay: boolean;
  recordUrl?: string | null; canEdit?: boolean; attendeeNames?: string[] | null;
};
export type MyDay = { overdue: number; dueToday: number; tasks: TaskRow[]; appointments: AppointmentRow[] };
export type OpportunityRow = {
  id: string; number: string; name: string; stageName: string; stageColor: string; outcome: string;
  contactName: string; address: string | null; ownerName: string | null; expectedValue: number | null; isStale: boolean;
};
export type Paged<T> = { items: T[]; total: number; page: number; pageSize: number };
export type ContactMethod = { kind: string; value: string; isPrimary: boolean };
export type Opportunity = {
  id: string; number: string; name: string; stageName: string; stageColor: string; outcome: string; expectedValue: number | null;
  ownerName: string | null; contactId: string; contactName: string; contactMethods: ContactMethod[];
  propertyId: string | null; address: string | null; description: string | null; canEdit: boolean; isInsurance?: boolean; sourceKey?: string | null;
};
export type TimelineItem = {
  itemType: string; id: string; at: string; authorName: string | null; text: string; entityType: string; entityId: string;
  fileName: string | null; subject: string | null; direction: string | null;
};
export type DocumentItem = { id: string; fileName: string; contentType: string; sizeBytes: number; category: string | null; createdAt: string };

// ---------- Errors ----------

/** A refusal from the server, with the message to show (ProblemDetails "detail"). */
export class ApiError extends Error {
  constructor(public status: number, message: string, public body?: unknown) {
    super(message);
  }
}

async function problem(res: Response): Promise<ApiError> {
  let body: unknown;
  try {
    body = await res.json();
  } catch {
    body = undefined;
  }
  const b = body as { detail?: string; title?: string } | undefined;
  const fallback = res.status === 404 ? "That wasn't found, or you don't have access to it." : `The server said ${res.status}.`;
  return new ApiError(res.status, b?.detail || b?.title || fallback, body);
}

// ---------- Client ----------

const KEY_SERVER = 'pich.server';
const KEY_TOKENS = 'pich.tokens';

/** The server address without a trailing slash, e.g. https://app.pich.example or http://192.168.1.20:5169 in development. */
export function normalizeServer(raw: string): string {
  let s = raw.trim().replace(/\/+$/, '');
  if (s && !/^https?:\/\//i.test(s)) s = `https://${s}`;
  return s;
}

export const savedServer = async () => (await storage.get(KEY_SERVER)) ?? process.env.EXPO_PUBLIC_API_URL ?? '';
export const saveServer = (server: string) => storage.set(KEY_SERVER, server);

export async function loadTokens(): Promise<Tokens | null> {
  const raw = await storage.get(KEY_TOKENS);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as Tokens;
  } catch {
    return null;
  }
}
const saveTokens = (t: Tokens) => storage.set(KEY_TOKENS, JSON.stringify(t));
export const clearTokens = () => storage.remove(KEY_TOKENS);

type SignInResult = { kind: 'ok'; tokens: Tokens } | { kind: 'choose'; tenants: TenantOption[] };

/** Email and password; with several companies, the server asks which one (409 choose_tenant). */
export async function signIn(server: string, email: string, password: string, tenantId?: string): Promise<SignInResult> {
  let res: Response;
  try {
    res = await fetch(`${server}/api/v1/auth/token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify({ email, password, tenantId: tenantId ?? null }),
    });
  } catch {
    throw new ApiError(0, `Couldn't reach ${server}. Check the server address and that you're online.`);
  }
  if (res.status === 409) {
    const body = (await res.json()) as { tenants: TenantOption[] };
    return { kind: 'choose', tenants: body.tenants };
  }
  if (!res.ok) throw await problem(res);
  const tokens = (await res.json()) as Tokens;
  await saveTokens(tokens);
  return { kind: 'ok', tokens };
}

/** A multipart body with one file, for photo uploads. */
export async function fileForm(uri: string, fileName: string, mimeType: string): Promise<FormData> {
  const form = new FormData();
  if (Platform.OS === 'web') {
    const blob = await (await fetch(uri)).blob();
    form.append('file', blob, fileName);
  } else {
    // React Native's FormData takes a file reference ({ uri, name, type }) instead of a Blob.
    form.append('file', { uri, name: fileName, type: mimeType } as unknown as Blob);
  }
  return form;
}

/**
 * Calls the API as the signed-in user. An expired access token is refreshed once (refresh tokens last 30 days);
 * if that's refused too, onSignedOut runs and the call fails.
 */
export class Api {
  private refreshing: Promise<boolean> | null = null;

  constructor(public server: string, private tokens: Tokens, private onSignedOut: () => void) {}

  async json<T>(path: string, init: RequestInit = {}): Promise<T> {
    const res = await this.send(path, init);
    if (res.status === 204) return undefined as T;
    return (await res.json()) as T;
  }

  get = <T>(path: string) => this.json<T>(path);
  post = <T>(path: string, body: unknown) =>
    this.json<T>(path, { method: 'POST', body: JSON.stringify(body), headers: { 'Content-Type': 'application/json' } });
  put = <T>(path: string, body: unknown) =>
    this.json<T>(path, { method: 'PUT', body: JSON.stringify(body), headers: { 'Content-Type': 'application/json' } });

  /** The current access token, for downloads that send their own request (file previews). */
  authHeader = () => ({ Authorization: `Bearer ${this.tokens.accessToken}` });

  async send(path: string, init: RequestInit, retried = false): Promise<Response> {
    let res: Response;
    try {
      res = await fetch(`${this.server}${path}`, {
        ...init,
        headers: { Accept: 'application/json', ...(init.headers ?? {}), ...this.authHeader() },
      });
    } catch {
      throw new ApiError(0, "Couldn't reach the server. Check your connection and try again.");
    }
    if (res.status === 401 && !retried) {
      if (await this.refresh()) return this.send(path, init, true);
      this.onSignedOut();
      throw new ApiError(401, 'Please sign in again.');
    }
    if (!res.ok) throw await problem(res);
    return res;
  }

  /** One refresh at a time, shared by every call that hit the expiry. */
  refresh(): Promise<boolean> {
    this.refreshing ??= (async () => {
      try {
        const res = await fetch(`${this.server}/api/v1/auth/refresh`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
          body: JSON.stringify({ refreshToken: this.tokens.refreshToken }),
        });
        if (!res.ok) return false;
        this.tokens = (await res.json()) as Tokens;
        await saveTokens(this.tokens);
        return true;
      } catch {
        return false;
      } finally {
        this.refreshing = null;
      }
    })();
    return this.refreshing;
  }
}

export type Option = { value: string; label: string };
export type StageOption = { id: string; name: string; color: string; outcome: string };
/** GET /api/v1/options: what the app's forms choose from. */
export type Options = { stages: StageOption[]; lostReasons: Option[]; appointmentTypes: Option[]; leadSources: Option[] };

export type SearchHit = { kind: 'contact' | 'organization' | 'property' | 'opportunity' | string; id: string; title: string; subtitle: string | null };
export type OpportunityBrief = { id: string; number: string; name: string; stageName: string; stageColor: string; outcome: string; address: string | null };
export type Link = { id: string; label: string; role: string | null };
export type ContactDetail = {
  id: string; name: string; type: string; ownerName: string | null; methods: ContactMethod[]; properties: Link[];
  opportunities: OpportunityBrief[]; canEdit: boolean; firstName?: string | null; lastName?: string | null; organizationName?: string | null;
};
export type PropertyContact = { id: string; name: string; role: string; isPrimary: boolean; phone: string | null; email: string | null };
export type PropertyDetail = { id: string; address: string; contacts: PropertyContact[]; opportunities: OpportunityBrief[] };
export type NotificationItem = { id: string; kind: string; title: string; body: string | null; url: string | null; createdAt: string; read: boolean };
export type Bell = { unread: number; items: NotificationItem[] };
export type WorkStage = { id: string; name: string; color: string; role: string };
export type StageHistory = { at: string; from: string | null; to: string; by: string | null; isAutomatic: boolean; note: string | null };
export type ProjectItem = {
  id: string; number: string; name: string; stageName: string; stageColor: string; contactName: string; address: string | null;
  trades: number; tradesComplete: number; isStale: boolean;
};
export type ScheduledTrade = {
  id: string; projectId: string; projectNumber: string; projectName: string; name: string; color: string; stageName: string; stageColor: string;
  address: string | null; materialDate: string | null; workDate: string | null; isStale: boolean;
};
export type TradeSummary = {
  id: string; name: string; color: string; stageId: string; stageName: string; stageColor: string; materialDate: string | null;
  workDate: string | null; crewNote: string | null; isStale: boolean; isComplete: boolean;
};
export type ProjectDetail = {
  id: string; number: string; name: string; stage: WorkStage; opportunityId: string; contactId: string; contactName: string;
  contactMethods: ContactMethod[]; propertyId: string | null; address: string | null; ownerName: string | null; notes: string | null;
  trades: TradeSummary[]; stages: WorkStage[]; history: StageHistory[]; canAdvance: boolean; isRemoved: boolean;
};
export type TradeDetail = {
  id: string; name: string; color: string; projectId: string; projectNumber: string; projectName: string; stage: WorkStage;
  description: string | null; materialDate: string | null; workDate: string | null; crewNote: string | null; contactId: string; contactName: string;
  contactMethods: ContactMethod[]; propertyId: string | null; address: string | null; stages: WorkStage[]; history: StageHistory[];
  canAdvance: boolean; isRemoved: boolean;
};
export type MovePreview = { allowed: boolean; blocker: string | null; automations: string[] };
export type Disposition = { id: string; name: string; color: string; recycleDays: number | null; isDoNotContact: boolean };
export type Campaign = { id: string; name: string; mode: string };
export type QueueCounts = { due: number; beingWorked: number; callBacksLater: number };
export type CallRow = { at: string; disposition: string; color: string; by: string | null; note: string | null };
export type ProspectCard = {
  id: string; number: string; name: string; stageName: string; stageColor: string; contactId: string; contactName: string;
  methods: ContactMethod[]; propertyId: string | null; address: string | null; ownerName: string | null; stormName: string | null;
  attempts: number; lastDisposition: string | null; calls: CallRow[]; lockedBy: string | null; canEdit: boolean;
};
export type Recipient = { contactId: string; contactName: string; kind: string; address: string; display: string; isPrimary: boolean };
export type MessageTemplate = { id: string; name: string; subject: string | null; body: string; category: string };
export type Sent = { messageId: string; ok: boolean; error: string | null };
export type MessageFull = {
  channel: 'sms' | 'email'; id: string; direction: 'inbound' | 'outbound' | string; at: string; senderName: string | null; counterparty: string;
  subject: string | null; body: string; status: string; error: string | null; cc: string | null; attachments: { documentId: string; fileName: string }[];
};
export type UnmatchedThread = {
  number: string; display: string; unread: number; lastAt: string;
  messages: { id: string; direction: 'inbound' | 'outbound' | string; body: string; at: string; status: string }[];
  possible: { id: string; name: string }[];
};
export type ChangeOrderItem = {
  id: string; sequence: number; description: string; amount: number; status: 'draft' | 'approved' | 'rejected' | string; isItemized: boolean;
  isInsuranceSupplement: boolean; tradeId: string | null; approvedByName: string | null; decidedAt: string | null;
};
export type ChangeOrderList = { canAdd: boolean; contractValue: number; items: ChangeOrderItem[] };
export type ChangeOrderDetail = {
  id: string; sequence: number; projectId: string; projectNumber: string; customerName: string; address: string | null; tradeName: string | null;
  description: string; reason: string | null; amount: number; status: string; isItemized: boolean; isInsuranceSupplement: boolean;
  approvedByName: string | null; decidedAt: string | null;
  lines: { id: string; code: string | null; description: string; qty: number; unit: string; unitPrice: number; amount: number }[]; canDecide: boolean;
};
export type AssistantHome = { configured: boolean; conversations: { id: string; title: string; lastMessageAt: string }[] };
export type ChatLine = { role: 'user' | 'assistant' | string; text: string; at: string; tools: string[] };
export type Conversation = {
  id: string; title: string; entityType: string | null; entityId: string | null; recordLabel: string | null; recordUrl: string | null; lines: ChatLine[];
};
export type Storm = {
  id: string; name: string; eventDate: string; perilKey: string; maxHailSizeIn: number | null; maxWindMph: number | null; zipCount: number; opportunities: number;
};
/** A house on a storm's canvass list; miles from the phone when it shared its position and the house is mapped. */
export type CanvassDoor = {
  propertyId: string; line1: string; line2: string | null; cityLine: string; postalCode: string; latitude: number | null; longitude: number | null; miles: number | null;
};
export type MeasureField = { key: string; label: string; unit: string; group: string | null };
export type MeasureTrade = { id: string; name: string; color: string; mode: string; surfaces: string[]; fields: MeasureField[] };
export type SetSummary = {
  id: string; structureId: string; structureName: string; tradeTypeId: string; tradeName: string; tradeColor: string; versionNo: number;
  versionCount: number; source: string; sourceLabel: string; measuredOn: string | null; measuredBy: string | null; headline: string;
};
export type PropertyMeasurements = {
  propertyId: string; address: string; structures: { id: string; name: string }[]; trades: MeasureTrade[]; current: SetSummary[]; canMeasure: boolean;
};
/** Values are keyed "surface|field": "|total_area" for the whole structure, "front|wall_area" for a side. */
export type MeasurementSet = {
  id: string; propertyId: string; structureId: string; structureName: string; tradeTypeId: string; tradeName: string; tradeColor: string; mode: string;
  versionNo: number; isCurrent: boolean; source: string; sourceLabel: string; measuredOn: string | null; measuredBy: string | null; notes: string | null;
  surfaces: string[]; fields: MeasureField[]; values: Record<string, number>; previous: Record<string, number>;
  reportDocumentId: string | null; reportFileName: string | null;
  versions: { id: string; versionNo: number; isCurrent: boolean; sourceLabel: string; measuredOn: string | null }[]; canEdit: boolean;
};
export type Person = { contactId: string; name: string; methods: ContactMethod[] };
export type WorkOrderItem = {
  id: string; number: string; status: string; acceptStatus: string; subName: string; projectId: string; projectNumber: string; tradeId: string;
  tradeName: string; address: string | null; workDate: string | null; amount: number;
};
export type WorkOrderDetail = {
  id: string; number: string; status: 'draft' | 'issued' | 'completed' | 'paid' | 'cancelled' | string; acceptStatus: 'pending' | 'accepted' | 'declined' | string;
  acceptedName: string | null; acceptedAt: string | null; declineReason: string | null; amount: number; workDate: string | null; arrivalTime: string | null;
  instructions: string | null; issuedAt: string | null; pdfDocumentId: string | null; completedAt: string | null; completionNote: string | null;
  sub: Person; projectId: string; projectNumber: string; projectName: string; tradeId: string; tradeName: string; address: string | null;
  lines: { id: string; description: string; qty: number; unit: string; rate: number; amount: number }[]; canEdit: boolean;
};
export type ClaimItem = {
  id: string; claimNumber: string | null; status: string; carrierName: string | null; opportunityId: string; opportunityNumber: string; contactName: string;
  address: string | null; dateOfLoss: string | null; inspectionOn: string | null; approvedAmount: number | null; openSupplements: number;
};
export type Supplement = {
  id: string; reason: string; status: string; requestedAmount: number | null; approvedAmount: number | null; sentOn: string | null;
  respondedOn: string | null; submittedBy: string | null; notes: string | null;
};
export type ClaimDetail = {
  id: string; claimNumber: string | null; policyNumber: string | null; status: string; dateOfLoss: string | null; perilKey: string | null;
  deductible: number | null; filedOn: string | null; inspectionOn: string | null; approvedAmount: number | null; totalApproved: number | null;
  notes: string | null; opportunityId: string; opportunityNumber: string; opportunityName: string; contactId: string; contactName: string;
  propertyId: string | null; address: string | null;
  carrier: { name: string; phone: string | null; claimsEmail: string | null; claimsFax: string | null; website: string | null; preferredSubmission: string | null } | null;
  adjuster: Person | null; deskAdjuster: Person | null; stormName: string | null; supplements: Supplement[]; statuses: string[];
  canEdit: boolean; isArchived: boolean;
};
export type EstimateItem = {
  id: string; number: string; name: string; status: string; total: number; opportunityId: string; contactName: string; address: string | null;
  ownerName: string | null; latestVersionNo: number; pendingApprovals: number; updatedAt: string;
};
export type EstimateLine = {
  id: string; kind: string; code: string | null; description: string; qty: number; unit: string; unitPrice: number; amount: number;
  approvalStatus: 'none' | 'pending' | 'approved' | 'rejected' | string; notes: string | null;
};
export type EstimateSection = { id: string; name: string; tradeName: string | null; tradeColor: string | null; scopeOfWork: string | null; subtotal: number; lines: EstimateLine[] };
export type EstimateVersion = {
  id: string; versionNo: number; status: 'issued' | 'signed' | 'declined' | 'superseded' | string; total: number; issuedAt: string; issuedBy: string | null;
  revisionReason: string | null; signerName: string | null; signedAt: string | null; hasPdf: boolean;
};
export type EstimateDetail = {
  id: string; number: string; name: string; status: 'draft' | 'issued' | 'signed' | 'declined' | 'shelved' | string; method: string;
  opportunityId: string; opportunityName: string; contactId: string; contactName: string; address: string | null; ownerName: string | null;
  claimNumber: string | null; priceListName: string | null; expiresOn: string | null;
  subtotal: number; overheadPct: number; overhead: number; profitPct: number; profit: number; taxRate: number; tax: number; total: number;
  pendingApprovals: number; rejectedLines: number; canEdit: boolean; isArchived: boolean; sections: EstimateSection[]; versions: EstimateVersion[];
};
