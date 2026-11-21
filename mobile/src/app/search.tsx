import Ionicons from '@expo/vector-icons/Ionicons';
import { router, Stack } from 'expo-router';
import { useEffect, useState } from 'react';
import { Text, TextInput, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { Card, Loading, Message, Muted, Row, Screen, SectionTitle, styles } from '@/components/ui';
import { SearchHit } from '@/lib/api';
import { colors, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

const GROUPS: { kinds: string[]; title: string; icon: React.ComponentProps<typeof Ionicons>['name'] }[] = [
  { kinds: ['opportunity'], title: 'Leads', icon: 'briefcase-outline' },
  { kinds: ['contact', 'organization'], title: 'Contacts', icon: 'person-outline' },
  { kinds: ['property'], title: 'Properties', icon: 'home-outline' },
];

function open(hit: SearchHit) {
  if (hit.kind === 'opportunity') router.push(`/opportunity/${hit.id}`);
  else if (hit.kind === 'property') router.push(`/property/${hit.id}`);
  else router.push(`/contact/${hit.id}`);
}

/** Search everything the user can see: names, numbers, addresses, phones and emails. */
export default function Search() {
  const [q, setQ] = useState('');
  const [term, setTerm] = useState('');
  useEffect(() => {
    const t = setTimeout(() => setTerm(q.trim()), 300);
    return () => clearTimeout(t);
  }, [q]);

  return (
    <Screen>
      <Stack.Screen options={{ title: 'Search' }} />
      <TextInput value={q} onChangeText={setQ} autoFocus placeholder="Name, OPP number, address, phone or email" placeholderTextColor={colors.muted}
        autoCorrect={false} autoCapitalize="none" clearButtonMode="while-editing" returnKeyType="search" style={styles.input} accessibilityLabel="Search" />
      {term.length < 2 ? <Muted>Type at least two letters.</Muted> : <Results term={term} />}
    </Screen>
  );
}

function Results({ term }: { term: string }) {
  const r = useCached<SearchHit[]>(`/api/v1/search?q=${encodeURIComponent(term)}`, "Couldn't search.");
  if (!r.data) return r.error ? <Message text={r.error} /> : <Loading />;
  if (r.data.length === 0) return <Muted>Nothing matches “{term}”.</Muted>;
  return (
    <View style={{ gap: space.md }}>
      <OfflineNote offline={r.offline} savedAt={r.savedAt} />
      {GROUPS.map((g) => {
        const hits = r.data!.filter((h) => g.kinds.includes(h.kind));
        if (hits.length === 0) return null;
        return (
          <View key={g.title} style={{ gap: space.sm }}>
            <SectionTitle>{g.title}</SectionTitle>
            <Card>
              {hits.map((h, i) => (
                <Row key={`${h.kind}-${h.id}`} last={i === hits.length - 1} onPress={() => open(h)}>
                  <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.md }}>
                    <Ionicons name={g.icon} size={20} color={colors.slate} />
                    <View style={{ flex: 1 }}>
                      <Text style={styles.body} numberOfLines={1}>{h.title}</Text>
                      {h.subtitle && <Muted>{h.subtitle}</Muted>}
                    </View>
                  </View>
                </Row>
              ))}
            </Card>
          </View>
        );
      })}
    </View>
  );
}
