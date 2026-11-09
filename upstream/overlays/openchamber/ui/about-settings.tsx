import React from 'react';
import { useI18n } from '@/lib/i18n';
import { runtimeFetch } from '@/lib/runtime-fetch';
import { SettingsSection, SettingsFieldRow } from '@/components/sections/shared/SettingsSection';

type Product = { name: 'TwinCAT Assistant'; version: string | null };
type State = { kind: 'loading' } | { kind: 'failed' } | { kind: 'ready'; product: Product };

// Installation and updates belong to the Windows installer. This page reports
// the actual product version, not the pinned Chat dependency's version.
export const AboutSettings: React.FC<{ initialUpdateDialogOpen?: boolean }> = () => {
  const { t } = useI18n();
  const [state, setState] = React.useState<State>({ kind: 'loading' });
  React.useEffect(() => {
    let active = true;
    void (async () => {
      try {
        const response = await runtimeFetch('/api/twincat/product');
        if (!response.ok) throw new Error('Product metadata request failed');
        const value: unknown = await response.json();
        if (typeof value !== 'object' || value === null || !('name' in value) || value.name !== 'TwinCAT Assistant'
            || !('version' in value) || (value.version !== null && (typeof value.version !== 'string' || !/^[0-9A-Za-z.+-]+$/.test(value.version))))
          throw new Error('Invalid product metadata');
        if (active) setState({ kind: 'ready', product: { name: value.name, version: value.version } });
      } catch { if (active) setState({ kind: 'failed' }); }
    })();
    return () => { active = false; };
  }, []);
  return (
    <SettingsSection title="TwinCAT Assistant" divider={false}>
      {state.kind === 'loading' ? <p>{t('common.loading')}</p> : state.kind === 'failed' ? <p role="alert">{t('common.unavailable')}</p> : (
        <SettingsFieldRow label={t('settings.openchamber.about.field.version')}>
          <span>{state.product.version ?? t('settings.openchamber.about.state.unknown')}</span>
        </SettingsFieldRow>
      )}
      <p className="typography-meta text-muted-foreground">TwinCAT Assistant Core · TwinCAT Assistant Chat · TwinCAT Assistant Host</p>
      <p className="typography-meta">
        <a href="https://github.com/Auda29/twincat-assistant" target="_blank" rel="noopener noreferrer">TwinCAT Assistant</a>
        {' · '}<a href="https://github.com/anomalyco/opencode" target="_blank" rel="noopener noreferrer">OpenCode</a>
        {' · '}<a href="https://github.com/openchamber/openchamber" target="_blank" rel="noopener noreferrer">OpenChamber</a>
      </p>
    </SettingsSection>
  );
};
