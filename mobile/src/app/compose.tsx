import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Text, View } from 'react-native';

import { Button, Card, Chips, Field, Loading, Message, Muted, Screen, styles } from '@/components/ui';
import { MessageTemplate, Recipient, Sent } from '@/lib/api';
import { outcomeMessage, useOutbox } from '@/lib/outbox';
import { space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

const PHONE_KINDS = ['mobile', 'phone'];

/**
 * A text or email from the company's number or mailbox, so it lands on the record's timeline and the office sees it.
 * Consent, opt-out wording and merge fields ({{contact.first_name}}) are handled by the server. Without a signal it
 * waits in the outbox and sends when you're back online.
 */
export default function Compose() {
  const { entityType, entityId, channel, label } = useLocalSearchParams<{ entityType: string; entityId: string; channel: 'sms' | 'email'; label?: string }>();
  const sms = channel === 'sms';
  const outbox = useOutbox();
  const people = useCached<Recipient[]>(`/api/v1/records/${entityType}/${entityId}/recipients`, "Couldn't load who to send to.");
  const templates = useCached<MessageTemplate[]>(`/api/v1/messages/templates?channel=${channel}`, "Couldn't load templates.");
  const options = (people.data ?? []).filter((r) => (sms ? PHONE_KINDS.includes(r.kind) : r.kind === 'email'));
  const [picked, setPicked] = useState<string | null>(null);
  const to = options.find((r) => `${r.contactId}|${r.address}` === picked) ?? options[0];
  const [templateId, setTemplateId] = useState<string | null>(null);
  const [subject, setSubject] = useState('');
  const [body, setBody] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function applyTemplate(id: string | null) {
    setTemplateId(id);
    const t = templates.data?.find((x) => x.id === id);
    if (t) { setBody(t.body); if (t.subject) setSubject(t.subject); }
  }

  async function send() {
    if (!to) return;
    if (!body.trim() || (!sms && !subject.trim())) { setError(sms ? 'Write a message.' : 'Add a subject and a message.'); return; }
    setBusy(true);
    setError(null);
    const outcome = await outbox.enqueue({
      kind: 'message', method: 'POST', path: `/api/v1/messages/${sms ? 'sms' : 'email'}`,
      json: { entityType, entityId, contactId: to.contactId, to: to.address, body: body.trim(), subject: sms ? undefined : subject.trim(), templateId },
      label: `${sms ? 'Text' : 'Email'} to ${to.contactName}${label ? ` (${label})` : ''}`,
    });
    setBusy(false);
    if (outcome.status === 'sent' && (outcome.body as Sent | undefined)?.ok === false) {
      setError((outcome.body as Sent).error ?? "It couldn't be sent. Try again in a minute.");
      return;
    }
    const m = outcomeMessage(outcome, sms ? 'Text sent.' : 'Email sent.');
    if (m.kind === 'danger') { setError(m.text); return; }
    router.back();
  }

  return (
    <Screen>
      <Stack.Screen options={{ title: sms ? 'Send a text' : 'Send an email' }} />
      {!people.data ? (people.error ? <Message text={people.error} /> : <Loading />) : options.length === 0 ? (
        <Message text={sms ? 'No mobile or phone number on file for this record.' : 'No email address on file for this record.'} />
      ) : (
        <>
          <Card style={{ gap: space.sm }}>
            <Text style={styles.label}>To</Text>
            <Chips value={to ? `${to.contactId}|${to.address}` : null} onChange={setPicked}
              options={options.map((r) => ({ label: `${r.contactName} · ${r.display}`, value: `${r.contactId}|${r.address}` as string | null }))} />
            <Muted>{sms ? "From the company's texting number. It goes on the record's timeline." : "From your company email. It goes on the record's timeline."}</Muted>
          </Card>
          {(templates.data?.length ?? 0) > 0 && (
            <View style={{ gap: space.sm }}>
              <Text style={styles.label}>Start from a template</Text>
              <Chips scroll value={templateId} onChange={applyTemplate}
                options={[{ label: 'None', value: null }, ...templates.data!.map((t) => ({ label: t.name, value: t.id as string | null }))]} />
            </View>
          )}
          {!sms && <Field label="Subject" value={subject} onChangeText={setSubject} placeholder="Your roof inspection" />}
          <Field label="Message" value={body} onChangeText={setBody} multiline style={{ minHeight: 140, textAlignVertical: 'top' }}
            placeholder={sms ? 'Hi {{contact.first_name}}, on my way now.' : 'Hi {{contact.first_name}},'} maxLength={sms ? 1600 : undefined} />
          {sms && <Muted>{`${body.length} characters. The first text to a number adds how to opt out.`}</Muted>}
          <Message text={error} />
          <Button title={sms ? 'Send text' : 'Send email'} onPress={send} busy={busy} disabled={!body.trim()} />
        </>
      )}
    </Screen>
  );
}
