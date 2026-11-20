import { router } from 'expo-router';
import { useEffect, useState } from 'react';
import { Text, TextInput, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { Card, Chips, Loading, Message, Muted, Row, Screen, SectionTitle, styles } from '@/components/ui';
import { ProjectItem, ScheduledTrade } from '@/lib/api';
import { day, todayIso } from '@/lib/format';
import { colors, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

type Mode = 'schedule' | 'projects';

/** Production for crews and project managers: open trades by work date, or projects by search. Saved for offline. */
export default function Jobs() {
  const [view, setView] = useState<Mode>('schedule');
  const [mine, setMine] = useState(false);
  const [refreshKey, setRefreshKey] = useState(0);
  const [q, setQ] = useState('');
  const [search, setSearch] = useState('');
  useEffect(() => {
    const t = setTimeout(() => setSearch(q.trim()), 350);
    return () => clearTimeout(t);
  }, [q]);

  return (
    <Screen refreshing={false} onRefresh={() => setRefreshKey((k) => k + 1)}>
      <Chips value={view} onChange={setView} options={[{ label: 'Schedule', value: 'schedule' }, { label: 'Projects', value: 'projects' }]} />
      <Chips value={mine} onChange={setMine} options={[{ label: 'Everyone I can see', value: false }, { label: 'My projects', value: true }]} />
      {view === 'schedule' ? <Schedule key={refreshKey} mine={mine} /> : (
        <>
          <TextInput value={q} onChangeText={setQ} placeholder="Name, P number or address" placeholderTextColor={colors.muted}
            autoCorrect={false} clearButtonMode="while-editing" style={styles.input} accessibilityLabel="Search projects" />
          <Projects key={refreshKey} mine={mine} q={search} />
        </>
      )}
    </Screen>
  );
}

function Schedule({ mine }: { mine: boolean }) {
  const r = useCached<ScheduledTrade[]>(`/api/v1/trades?mine=${mine}`, "Couldn't load the schedule.");
  if (!r.data) return r.error ? <Message text={r.error} /> : <Loading />;
  const today = todayIso();
  const groups: { title: string; items: ScheduledTrade[] }[] = [];
  for (const t of r.data) {
    const title = !t.workDate ? 'Not scheduled' : t.workDate < today ? 'Past work date' : t.workDate === today ? 'Today' : day(t.workDate);
    const g = groups.find((x) => x.title === title);
    if (g) g.items.push(t); else groups.push({ title, items: [t] });
  }
  return (
    <View style={{ gap: space.md }}>
      <OfflineNote offline={r.offline} savedAt={r.savedAt} />
      {r.data.length === 0 && <Card><Muted>No open trades on the board.</Muted></Card>}
      {groups.map((g) => (
        <View key={g.title} style={{ gap: space.sm }}>
          <SectionTitle>{g.title}</SectionTitle>
          <Card>
            {g.items.map((t, i) => (
              <Row key={t.id} last={i === g.items.length - 1} onPress={() => router.push(`/trade/${t.id}`)}>
                <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.sm }}>
                  <View style={{ width: 4, alignSelf: 'stretch', borderRadius: 2, backgroundColor: t.color }} />
                  <View style={{ flex: 1 }}>
                    <Text style={styles.body} numberOfLines={1}>{t.name} · {t.projectName}</Text>
                    <Muted>{[t.projectNumber, t.address].filter(Boolean).join(' · ')}</Muted>
                    <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.xs, marginTop: 2 }}>
                      <View style={{ width: 8, height: 8, borderRadius: 4, backgroundColor: t.stageColor }} />
                      <Muted>{[t.stageName, t.materialDate ? `materials ${day(t.materialDate)}` : null, t.isStale ? 'needs attention' : null].filter(Boolean).join(' · ')}</Muted>
                    </View>
                  </View>
                </View>
              </Row>
            ))}
          </Card>
        </View>
      ))}
    </View>
  );
}

function Projects({ mine, q }: { mine: boolean; q: string }) {
  const query = new URLSearchParams({ mine: String(mine) });
  if (q) query.set('q', q);
  const r = useCached<ProjectItem[]>(`/api/v1/projects?${query}`, "Couldn't load projects.");
  if (!r.data) return r.error ? <Message text={r.error} /> : <Loading />;
  return (
    <View style={{ gap: space.md }}>
      <OfflineNote offline={r.offline} savedAt={r.savedAt} />
      <Card>
        {r.data.length === 0 && <Muted>No projects{q ? ' match' : ''}.</Muted>}
        {r.data.map((p, i) => (
          <Row key={p.id} last={i === r.data!.length - 1} onPress={() => router.push(`/project/${p.id}`)}>
            <Text style={styles.title} numberOfLines={1}>{p.contactName}</Text>
            <Muted>{[p.number, p.address].filter(Boolean).join(' · ')}</Muted>
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.xs, marginTop: 4 }}>
              <View style={{ width: 8, height: 8, borderRadius: 4, backgroundColor: p.stageColor || colors.slate }} />
              <Text style={{ color: colors.slate, fontSize: 13 }}>
                {[p.stageName, `${p.tradesComplete}/${p.trades} trades done`, p.isStale ? 'needs attention' : null].filter(Boolean).join(' · ')}
              </Text>
            </View>
          </Row>
        ))}
      </Card>
    </View>
  );
}
