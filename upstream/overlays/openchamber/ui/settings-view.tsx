// Product settings use the upstream shared controls and persistence contracts.
// Project access, MCP setup and diagnostics are owned by the native Assistant.
import React from 'react';
import { useUIStore } from '@/stores/useUIStore';
import { useDeviceInfo } from '@/lib/device';
import { useI18n } from '@/lib/i18n';
import { resolveSettingsSlug, type SettingsPageSlug } from '@/lib/settings/metadata';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { ErrorBoundary } from '@/components/ui/ErrorBoundary';
import { ScrollableOverlay } from '@/components/ui/ScrollableOverlay';
import { SettingsPageLayout } from '@/components/sections/shared/SettingsPageLayout';
import { ProvidersPage } from '@/components/sections/providers/ProvidersPage';
import { OpenChamberVisualSettings } from '@/components/sections/openchamber/OpenChamberVisualSettings';
import { SessionRetentionSettings } from '@/components/sections/openchamber/SessionRetentionSettings';
import { KeyboardShortcutsSettings } from '@/components/sections/openchamber/KeyboardShortcutsSettings';
import { AboutSettings } from '@/components/sections/openchamber/AboutSettings';

interface SettingsViewProps {
  onClose?: () => void;
  forceMobile?: boolean;
  isWindowed?: boolean;
  visiblePageSlugs?: SettingsPageSlug[];
  registerBackHandler?: (handler: (() => boolean) | null) => void;
  initialMobileStage?: 'nav' | 'page-sidebar' | 'page-content';
}

const pages = ['providers', 'appearance', 'chat', 'sessions', 'shortcuts', 'about'] as const;
type ProductPage = typeof pages[number];
const isProductPage = (value: string): value is ProductPage => pages.some(page => page === value);

export const SettingsView: React.FC<SettingsViewProps> = ({ onClose, forceMobile, visiblePageSlugs, registerBackHandler, initialMobileStage = 'nav' }) => {
  const { t } = useI18n();
  const device = useDeviceInfo();
  const [compact, setCompact] = React.useState(() => window.matchMedia('(max-width: 640px)').matches);
  React.useEffect(() => {
    const media = window.matchMedia('(max-width: 640px)');
    const update = () => setCompact(media.matches);
    media.addEventListener('change', update);
    return () => media.removeEventListener('change', update);
  }, []);
  const isMobile = forceMobile ?? (device.isMobile || compact);
  const rawPage = useUIStore(state => state.settingsPage);
  const setPage = useUIStore(state => state.setSettingsPage);
  const slug = resolveSettingsSlug(rawPage);
  const allowed = pages.filter(page => !visiblePageSlugs || visiblePageSlugs.includes(page));
  const page = isProductPage(slug) && allowed.includes(slug) ? slug : (allowed[0] ?? 'about');
  const [query, setQuery] = React.useState('');
  const [showContent, setShowContent] = React.useState(initialMobileStage !== 'nav');
  const title = (value: ProductPage) => t(`settings.page.${value}.title`);

  React.useEffect(() => {
    if (rawPage !== 'home') setShowContent(true);
  }, [rawPage]);
  React.useEffect(() => {
    registerBackHandler?.(() => {
      if (!isMobile || !showContent) return false;
      setShowContent(false);
      return true;
    });
    return () => registerBackHandler?.(null);
  }, [isMobile, showContent, registerBackHandler]);

  const renderContent = () => {
    if (page === 'providers') return <ProvidersPage />;
    return <SettingsPageLayout title={title(page)} showSaveStatus={page !== 'about'}>
      {page === 'appearance' && <OpenChamberVisualSettings visibleSettings={[
        'theme', 'timeFormat', 'weekStart', 'fontSize', 'spacing', 'scrollbars', 'animatedActivityIndicators',
      ]} />}
      {page === 'chat' && <OpenChamberVisualSettings visibleSettings={[
        'chatRenderMode', 'activityRenderMode', 'userMessageRendering', 'mermaidRendering', 'reasoning',
        'showToolFileIcons', 'expandedTools', 'collapsibleUserMessages', 'stickyUserHeader',
        'promptNavigatorEnabled', 'wideChatLayout', 'codeBlockLineWrap', 'tableCellWrap',
        'copyMessagesAsPlainText', 'splitAssistantMessageActions', 'inputHistoryScope', 'inputHistoryLimit',
        'messageSearch', 'followUpBehavior', 'persistDraft', 'inputSpellcheck', 'largeTextPaste', 'enterToSend',
      ]} />}
      {page === 'sessions' && <SessionRetentionSettings />}
      {page === 'shortcuts' && <KeyboardShortcutsSettings />}
      {page === 'about' && <AboutSettings />}
    </SettingsPageLayout>;
  };

  return <div className="flex h-full min-h-0 flex-col bg-background">
    <div className="flex shrink-0 items-center justify-between border-b border-border px-4 py-2">
      <span className="typography-ui font-medium">TwinCAT Assistant</span>
      {onClose && <Button variant="ghost" size="sm" onClick={onClose}>{t('contextPanel.tab.menu.close')}</Button>}
    </div>
    <div className="flex min-h-0 flex-1">
      {(!isMobile || !showContent) && <nav className={isMobile ? 'flex w-full min-h-0 flex-col' : 'flex w-52 shrink-0 min-h-0 flex-col border-r border-border'} aria-label={t('settings.view.search.aria')}>
        <div className="p-3"><Input value={query} onChange={event => setQuery(event.target.value)} placeholder={t('settings.view.search.placeholder')} aria-label={t('settings.view.search.aria')} /></div>
        <ScrollableOverlay className="min-h-0 flex-1 px-2">
          {allowed.filter(value => title(value).toLocaleLowerCase().includes(query.toLocaleLowerCase())).map(value => <Button
            key={value} variant={value === page ? 'secondary' : 'ghost'} className="mb-1 w-full justify-start"
            aria-current={value === page ? 'page' : undefined}
            onClick={() => { setPage(value); setShowContent(true); }}
          >{title(value)}</Button>)}
          {allowed.every(value => !title(value).toLocaleLowerCase().includes(query.toLocaleLowerCase())) && <p className="p-3 typography-ui text-muted-foreground">{t('settings.view.search.noResults')}</p>}
        </ScrollableOverlay>
      </nav>}
      {(!isMobile || showContent) && <div className="flex min-w-0 flex-1 flex-col">
        {isMobile && <Button variant="ghost" className="m-2 self-start" onClick={() => setShowContent(false)}>{t('settings.view.home.title')}</Button>}
        <ErrorBoundary key={page}>{renderContent()}</ErrorBoundary>
      </div>}
    </div>
  </div>;
};
