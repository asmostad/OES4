import * as SecureStore from 'expo-secure-store';

/** Sign-in tokens and settings, kept in the phone's secure storage (Keychain / Keystore). */
export const storage = {
  get: (key: string) => SecureStore.getItemAsync(key),
  set: (key: string, value: string) => SecureStore.setItemAsync(key, value),
  remove: (key: string) => SecureStore.deleteItemAsync(key),
};
