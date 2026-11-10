// TwinCAT Assistant composer: files, model selection, send, queue and stop only.
import React from 'react';
import type { SourceControlProvider } from '@/lib/api/types';
import type { GuestAttachItem } from '@/hooks/useGuestSurfaces';
import type { PermissionMode } from '@/stores/utils/permissionAutoAccept';
import type { BtwSelection } from '@/stores/useBtwStore';
import { useI18n } from '@/lib/i18n';
import { cn } from '@/lib/utils';
import { ModelControls } from '../../ModelControls';
import { ComposerActionButtons } from './ComposerActionButtons';
import { ComposerAttachmentControls } from './ComposerAttachmentControls';
import { FocusModeButton } from './FocusModeButton';
const MemoModelControls = React.memo(ModelControls);
export interface ComposerFooterProps {
    isMobile: boolean;
    isVSCode: boolean;
    sessionId: string | null;
    directory?: string;
    newSessionDraftOpen: boolean;
    messageLength: number;

    radius: string;
    footerPaddingClass: string;
    footerGapClass: string;
    footerIconButtonClass: string;
    iconSizeClass: string;
    sendIconSizeClass: string;
    stopIconSizeClass: string;

    canSend: boolean;
    canAbort: boolean;
    hasContent: boolean;
    isExpandedInput: boolean;
    permissionMode: PermissionMode;
    isPermissionAutoAcceptInteractive: boolean;
    dictationActive: boolean;

    onOpenSettings?: () => void;
    onPickLocalFiles: () => void;
    onOpenGitHubPicker: () => void;
    /** The host the project's issues and change requests come from. */
    repositoryProvider?: SourceControlProvider;
    showLinearPicker?: boolean;
    onOpenLinearPicker?: () => void;
    attachGuests?: readonly GuestAttachItem[];
    onOpenGuestAttach?: (guestId: string) => void;
    onOpenAttachSheet: () => void;
    onToggleExpandedInput: () => void;
    onCyclePermissionMode: () => void;
    onPrimaryAction: () => void;
    onQueueMessage: () => void;
    onAbort: () => void;
    onStartDictation: () => void;
    onDictationInsert: (text: string) => void;
    onDictationInsertAndSend: (text: string) => void;
    onDictationSendStart: () => void;
    onDictationStart: () => void;
    onDictationContentHeightChange: (height: number | null) => void;
    isBtw?: boolean;
    modelSessionId?: string | null;
    btwSelection: BtwSelection;
    /** A pinned column's own model, agent and effort (see pinnedComposerSelection.ts). */
    pinnedSelection?: BtwSelection | null;
    /** Offers "Run on several models" in the model picker (desktop). */
    onRunInParallel?: () => void;
    /** Set while the composer is in "Run in parallel" mode: the primary action launches the run. */
    parallelRun?: { runCount: number; launching: boolean; onLaunch: () => void } | null;
}


export function ComposerFooter(props: ComposerFooterProps) {
 const { t } = useI18n();
 const { isMobile, isVSCode, sessionId, newSessionDraftOpen, radius, footerPaddingClass, footerGapClass,
  footerIconButtonClass, iconSizeClass, sendIconSizeClass, stopIconSizeClass, canSend, canAbort, hasContent,
  isExpandedInput, onOpenSettings, onPickLocalFiles, onToggleExpandedInput, onPrimaryAction,
  onQueueMessage, onAbort, isBtw, modelSessionId, btwSelection, pinnedSelection, onRunInParallel, parallelRun } = props;
 return <div className={cn('bg-transparent flex shrink-0 flex-wrap items-center justify-between gap-2', footerPaddingClass, footerGapClass)}
  style={{ borderBottomLeftRadius: radius, borderBottomRightRadius: radius }} data-chat-input-footer="true">
  <div className="flex items-center gap-1">
   <ComposerAttachmentControls isVSCode={isVSCode} footerIconButtonClass={footerIconButtonClass} iconSizeClass={iconSizeClass}
    handlePickLocalFiles={onPickLocalFiles} openGitHubPicker={props.onOpenGitHubPicker} filesOnly onOpenSettings={isBtw ? undefined : onOpenSettings} />
   {!isBtw && <FocusModeButton footerIconButtonClass={footerIconButtonClass} iconSizeClass={iconSizeClass} isExpandedInput={isExpandedInput} onToggle={onToggleExpandedInput} />}
  </div>
  <div className="flex min-w-0 flex-1 items-center justify-end gap-2">
   {!parallelRun && (isBtw ? <ModelControls className="min-w-0 flex-1 justify-end" sessionId={modelSessionId ?? null} selection={btwSelection} />
    : pinnedSelection ? <ModelControls className="min-w-0 flex-1 justify-end" sessionId={sessionId} selection={pinnedSelection} agentSelectable />
    : <MemoModelControls className="min-w-0 flex-1 justify-end" onRunInParallel={onRunInParallel} />)}
   <ComposerActionButtons isMobile={isMobile} footerIconButtonClass={footerIconButtonClass} sendIconSizeClass={sendIconSizeClass} stopIconSizeClass={stopIconSizeClass}
    canSend={parallelRun ? canSend && parallelRun.runCount >= 2 && !parallelRun.launching : canSend} canAbort={parallelRun ? false : canAbort}
    hasContent={hasContent} currentSessionId={sessionId} newSessionDraftOpen={newSessionDraftOpen}
    onPrimaryAction={parallelRun ? parallelRun.onLaunch : onPrimaryAction} onQueueMessage={onQueueMessage} onAbort={onAbort}
    sendLabel={parallelRun ? t('chat.parallel.runAria', { count: parallelRun.runCount }) : undefined} />
  </div>
 </div>;
}
