import { Image } from 'expo-image';
import { useState } from 'react';
import { KeyboardAvoidingView, Platform, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';

import { Button, Field, Message } from '@/components/ui';
import { ApiError, normalizeServer, TenantOption } from '@/lib/api';
import { useSession } from '@/lib/session';
import { colors, radius, space } from '@/lib/theme';

export default function SignIn() {
  const session = useSession();
  const [server, setServer] = useState(session.status === 'signed-out' ? session.server : '');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [tenants, setTenants] = useState<TenantOption[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function go(tenantId?: string) {
    const address = normalizeServer(server);
    if (!address) return setError('Enter the Pich server address your company gave you.');
    if (!email.trim() || !password) return setError('Enter your email and password.');
    setBusy(true);
    setError(null);
    try {
      const choose = await session.signIn(address, email.trim(), password, tenantId);
      if (choose) setTenants(choose);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Sign-in failed. Try again.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <KeyboardAvoidingView style={{ flex: 1, backgroundColor: colors.navy }} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
      <ScrollView contentContainerStyle={styles.wrap} keyboardShouldPersistTaps="handled">
        <Image source={require('../../assets/images/splash-icon.png')} style={styles.logo} contentFit="contain" accessibilityLabel="Pich" />
        <View style={styles.card}>
          {tenants ? (
            <>
              <Text style={styles.heading}>Which company?</Text>
              <Text style={styles.hint}>Your account is in more than one company.</Text>
              {tenants.map((t) => (
                <Pressable key={t.id} onPress={() => go(t.id)} disabled={busy} style={({ pressed }) => [styles.tenant, pressed && { opacity: 0.6 }]}>
                  <Text style={styles.tenantName}>{t.name}</Text>
                </Pressable>
              ))}
              <Message text={error} />
              <Button title="Back" kind="secondary" onPress={() => setTenants(null)} />
            </>
          ) : (
            <>
              <Text style={styles.heading}>Sign in</Text>
              <Field label="Server" value={server} onChangeText={setServer} placeholder="pich.yourcompany.com" autoCapitalize="none"
                autoCorrect={false} keyboardType="url" textContentType="URL" />
              <Field label="Email" value={email} onChangeText={setEmail} autoCapitalize="none" autoCorrect={false}
                keyboardType="email-address" textContentType="username" autoComplete="email" />
              <Field label="Password" value={password} onChangeText={setPassword} secureTextEntry textContentType="password"
                autoComplete="current-password" onSubmitEditing={() => go()} returnKeyType="go" />
              <Message text={error} />
              <View style={{ height: space.md }} />
              <Button title="Sign in" onPress={() => go()} busy={busy} />
            </>
          )}
        </View>
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  wrap: { flexGrow: 1, justifyContent: 'center', padding: space.xl, gap: space.xl },
  logo: { width: 200, height: 70, alignSelf: 'center' },
  card: { backgroundColor: colors.white, borderRadius: radius, padding: space.xl, borderTopWidth: 4, borderTopColor: colors.orange },
  heading: { fontSize: 22, fontWeight: '700', color: colors.text, marginBottom: space.lg },
  hint: { color: colors.muted, marginBottom: space.md },
  tenant: { paddingVertical: space.md, borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: colors.border },
  tenantName: { fontSize: 17, color: colors.navy, fontWeight: '600' },
});
