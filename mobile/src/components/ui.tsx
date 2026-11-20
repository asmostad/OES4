import { ReactNode } from 'react';
import {
  ActivityIndicator, Pressable, RefreshControl, ScrollView, StyleSheet, Text, TextInput, TextInputProps, View, ViewStyle,
} from 'react-native';

import { colors, radius, space } from '@/lib/theme';

/** A scrolling screen with pull-to-refresh. */
export function Screen({ children, refreshing, onRefresh }: { children: ReactNode; refreshing?: boolean; onRefresh?: () => void }) {
  return (
    <ScrollView
      style={styles.screen}
      contentContainerStyle={styles.content}
      keyboardShouldPersistTaps="handled"
      refreshControl={onRefresh ? <RefreshControl refreshing={!!refreshing} onRefresh={onRefresh} tintColor={colors.navy} /> : undefined}>
      {children}
    </ScrollView>
  );
}

export function Card({ children, style }: { children: ReactNode; style?: ViewStyle }) {
  return <View style={[styles.card, style]}>{children}</View>;
}

export function SectionTitle({ children }: { children: ReactNode }) {
  return <Text style={styles.section}>{children}</Text>;
}

export function Muted({ children }: { children: ReactNode }) {
  return <Text style={styles.muted}>{children}</Text>;
}

export function Button({ title, onPress, kind = 'primary', busy, disabled }: {
  title: string; onPress: () => void; kind?: 'primary' | 'secondary' | 'danger'; busy?: boolean; disabled?: boolean;
}) {
  const off = disabled || busy;
  return (
    <Pressable
      accessibilityRole="button"
      onPress={off ? undefined : onPress}
      style={({ pressed }) => [styles.button, styles[kind], off && styles.off, pressed && !off && styles.pressed]}>
      {busy ? <ActivityIndicator color={kind === 'secondary' ? colors.navy : colors.white} />
        : <Text style={[styles.buttonText, kind === 'secondary' && styles.secondaryText]}>{title}</Text>}
    </Pressable>
  );
}

export function Field(props: TextInputProps & { label: string }) {
  const { label, style, ...rest } = props;
  return (
    <View style={{ marginBottom: space.md }}>
      <Text style={styles.label}>{label}</Text>
      <TextInput placeholderTextColor={colors.muted} style={[styles.input, style]} {...rest} />
    </View>
  );
}

export function Message({ text, kind = 'danger' }: { text: string | null | undefined; kind?: 'danger' | 'success' }) {
  if (!text) return null;
  return (
    <View style={[styles.message, kind === 'success' ? styles.messageOk : styles.messageBad]} accessibilityRole="alert">
      <Text style={{ color: kind === 'success' ? colors.success : colors.danger }}>{text}</Text>
    </View>
  );
}

export function Loading() {
  return (
    <View style={{ padding: space.xl, alignItems: 'center' }}>
      <ActivityIndicator color={colors.navy} />
    </View>
  );
}

/** A tappable row inside a card. */
export function Row({ children, onPress, last }: { children: ReactNode; onPress?: () => void; last?: boolean }) {
  return (
    <Pressable onPress={onPress} disabled={!onPress} style={({ pressed }) => [styles.row, !last && styles.rowLine, pressed && onPress && { opacity: 0.6 }]}>
      {children}
    </Pressable>
  );
}

export const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.cloud },
  content: { padding: space.lg, paddingBottom: space.xl * 2, gap: space.md },
  card: { backgroundColor: colors.white, borderRadius: radius, padding: space.lg, borderWidth: StyleSheet.hairlineWidth, borderColor: colors.border },
  section: { fontSize: 13, fontWeight: '700', color: colors.slate, textTransform: 'uppercase', letterSpacing: 0.6, marginTop: space.sm },
  muted: { color: colors.muted, fontSize: 14 },
  title: { fontSize: 17, fontWeight: '600', color: colors.text },
  body: { fontSize: 15, color: colors.text },
  button: { borderRadius: radius, paddingVertical: 12, paddingHorizontal: space.lg, alignItems: 'center', justifyContent: 'center', minHeight: 46 },
  primary: { backgroundColor: colors.navy },
  secondary: { backgroundColor: colors.white, borderWidth: 1, borderColor: colors.navy },
  danger: { backgroundColor: colors.danger },
  off: { opacity: 0.5 },
  pressed: { opacity: 0.8 },
  buttonText: { color: colors.white, fontWeight: '600', fontSize: 16 },
  secondaryText: { color: colors.navy },
  label: { fontSize: 13, fontWeight: '600', color: colors.slate, marginBottom: space.xs },
  input: {
    backgroundColor: colors.white, borderWidth: 1, borderColor: colors.border, borderRadius: radius,
    paddingHorizontal: space.md, paddingVertical: 10, fontSize: 16, color: colors.text,
  },
  message: { borderRadius: radius, padding: space.md },
  messageBad: { backgroundColor: colors.dangerBg },
  messageOk: { backgroundColor: colors.successBg },
  row: { paddingVertical: space.md },
  rowLine: { borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: colors.border },
});

/** A row of choices, one selected (due dates, stages, appointment types…). Wraps, or scrolls sideways with `scroll`. */
export function Chips<T extends string | number | null>({ options, value, onChange, scroll }: {
  options: { label: string; value: T }[]; value: T; onChange: (v: T) => void; scroll?: boolean;
}) {
  const items = options.map((o) => {
    const on = o.value === value;
    return (
      <Pressable key={`${o.label}-${String(o.value)}`} onPress={() => onChange(o.value)} accessibilityRole="radio" accessibilityState={{ selected: on }}
        style={{ paddingVertical: 7, paddingHorizontal: space.md, borderRadius: radius, borderWidth: 1,
          borderColor: on ? colors.navy : colors.border, backgroundColor: on ? colors.navy : colors.white }}>
        <Text style={{ color: on ? colors.white : colors.text }}>{o.label}</Text>
      </Pressable>
    );
  });
  return scroll
    ? <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={{ gap: space.sm }}>{items}</ScrollView>
    : <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: space.sm }}>{items}</View>;
}
