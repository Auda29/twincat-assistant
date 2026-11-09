import { installOpenCodeV2, supportsOpenCodeV2Install } from './lib/opencode/v2-install.js';
import { describeOpenCodeCompatibility, readOpenCodeCliVersion, readExternalOpenCodeVersion } from './lib/opencode/compatibility.js';
import 'reflect-metadata';
import express from 'express';
import compression from 'compression';
import path from 'path';
import { spawn } from 'child_process';
import fs from 'fs';
import http from 'http';
import { fileURLToPath } from 'url';
import os from 'os';
import crypto from 'crypto';
import { createUiAuth } from './lib/ui-auth/ui-auth.js';
import { createManagedTunnelConfigRuntime } from './lib/tunnels/managed-config.js';
import { allowsLocalDevOrigins, buildFrameAncestorsPolicy, createRequestSecurityRuntime, isLocalDevClientOrigin } from './lib/security/request-security.js';
import { readEnterprisePolicy } from './lib/enterprise-mode.js';
import { normalizeOptionalPath, normalizeTunnelMode, normalizeTunnelProvider } from './lib/tunnels/types.js';
import { createGlobalUiEventBroadcaster, translateWireEvent, createGlobalMessageStreamHub, createMessageStreamWsRuntime, resolveDeltaCoalesceWindowMs, DEFAULT_UPSTREAM_STALL_TIMEOUT_MS, UPSTREAM_STALL_TIMEOUT_CONCURRENT_MS } from './lib/event-stream/index.js';
import { createFsSearchRuntime as createFsSearchRuntimeFactory } from './lib/fs/search.js';
import { createOpenCodeLifecycleRuntime } from './lib/opencode/lifecycle.js';
import { createOpenCodeEnvRuntime } from './lib/opencode/env-runtime.js';
import { providedLoginShellEnvSnapshot } from './lib/opencode/login-shell-env.js';
import { resolveOpenCodeEnvConfig } from './lib/opencode/env-config.js';
import { createHmrStateRuntime } from './lib/opencode/hmr-state-runtime.js';
import { createOpenCodeNetworkRuntime } from './lib/opencode/network-runtime.js';
import { createOpenCodeAuthStateRuntime } from './lib/opencode/auth-state-runtime.js';
import { createProjectDirectoryRuntime } from './lib/opencode/project-directory-runtime.js';
import { createSettingsNormalizationRuntime } from './lib/opencode/settings-normalization-runtime.js';
import { createSettingsHelpers } from './lib/opencode/settings-helpers.js';
import { createThemeRuntime } from './lib/opencode/theme-runtime.js';
import { createFeatureRoutesRuntime } from './lib/opencode/feature-routes-runtime.js';
import { isRequestAuthorized, registerAuthAndAccessRoutes, registerCommonRequestMiddleware, registerServerStatusRoutes } from './lib/opencode/core-routes.js';
import { registerOpenChamberRoutes } from './lib/opencode/openchamber-routes.js';
import { createServerUtilsRuntime } from './lib/opencode/server-utils-runtime.js';
import { createStaticRoutesRuntime } from './lib/opencode/static-routes-runtime.js';
import { createSettingsRuntime } from './lib/opencode/settings-runtime.js';
import { createOpenCodeResolutionRuntime } from './lib/opencode/opencode-resolution-runtime.js';
import { runOpenCodeCliUpgrade } from './lib/opencode/cli-upgrade.js';
import { resolveOpenCodeUpgradeCapability } from './lib/opencode/upgrade-capability.js';
import { createBootstrapRuntime } from './lib/opencode/bootstrap-runtime.js';
import { createSessionRuntime } from './lib/opencode/session-runtime.js';
import { configureOpenCodeRuntimeProviders, resetOpenCodeRuntimeProviders } from './lib/small-model/client.js';
import { configureOpenCodeCredentials, openCodeCredentialSource } from './lib/opencode/auth.js';
import { createOpenCodeWatcherRuntime } from './lib/opencode/watcher.js';
import { createContextObligatoryRuntime } from './lib/context-obligatory/runtime.js';
import { createSessionKnowledgeRuntime } from './lib/session-knowledge/runtime.js';
import { createMessageSearchRuntime } from './lib/message-search/runtime.js';
import { createServerStartupRuntime } from './lib/opencode/server-startup-runtime.js';
import { createStartupPipelineRuntime } from './lib/opencode/startup-pipeline-runtime.js';
import { registerNotificationRoutes } from './lib/notifications/routes.js';
import { createNotificationEmitterRuntime } from './lib/notifications/emitter-runtime.js';
import { createNotificationTemplateRuntime } from './lib/notifications/template-runtime.js';
import { createMessageQueueRuntime } from './lib/message-queue/runtime.js';
import { createSessionLineage } from './lib/session-lineage.js';
import { createGracefulShutdownRuntime } from './lib/opencode/shutdown-runtime.js';
import { createProjectConfigRuntime } from './lib/projects/project-config.js';
import { migrateLegacyUserDirs } from './lib/data-dir-migration.js';
import { createProjectContextRuntime } from './lib/project-context/runtime.js';
import { createAgentMemoryRuntime } from './lib/agent-memory/runtime.js';
import { createMemoryProjectResolver } from './lib/agent-memory/project-resolution.js';
import { isAgentMemoryFeatureAvailable } from './lib/agent-memory/feature-flag.js';
import { resolvePrimaryWorktreeRoot } from './lib/git/service.js';
import { createWorktreeBootstrapStore } from './lib/git/worktree-bootstrap-storage.js';
// Browser control is outside the delivered Assistant Chat scope.
import { createManagedConfigRuntime } from './lib/opencode/managed-config-file.js';
import { createOpenChamberSessionService } from './lib/openchamber-sessions/routes.js';
import { createSessionMetadataStore, createOpenCodeSessionMetadata } from './lib/openchamber-sessions/session-metadata-store.js';
import { applyConnectAttemptTimeout } from './lib/network-defaults.js';
// Background CLI launches enter here in a fresh process, without CLI defaults.
applyConnectAttemptTimeout();
const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const DEFAULT_PORT = 3000;
const DESKTOP_NOTIFY_PREFIX = '[OpenChamberDesktopNotify] ';
const uiNotificationClients = new Set();
const uiNotificationWsClients = new Set();
const uiOpenChamberEventClients = new Set();
const HEALTH_CHECK_INTERVAL = 15000;
const SHUTDOWN_TIMEOUT = 10000;
const MODELS_DEV_API_URL = 'https://models.dev/api.json';
const MODELS_METADATA_CACHE_TTL = 5 * 60 * 1000;
const CLIENT_RELOAD_DELAY_MS = 800;
const OPEN_CODE_READY_GRACE_MS = 12000;
const LONG_REQUEST_TIMEOUT_MS = 4 * 60 * 1000;
const TUNNEL_BOOTSTRAP_TTL_DEFAULT_MS = 30 * 60 * 1000;
const TUNNEL_BOOTSTRAP_TTL_MIN_MS = 60 * 1000;
const TUNNEL_BOOTSTRAP_TTL_MAX_MS = 24 * 60 * 60 * 1000;
const TUNNEL_SESSION_TTL_DEFAULT_MS = 8 * 60 * 60 * 1000;
const TUNNEL_SESSION_TTL_MIN_MS = 5 * 60 * 1000;
const TUNNEL_SESSION_TTL_MAX_MS = 30 * 24 * 60 * 60 * 1000;
function headerIncludesEventStream(value) {
    if (typeof value === 'string') {
        return value.toLowerCase().includes('text/event-stream');
    }
    if (Array.isArray(value)) {
        return value.some((entry) => typeof entry === 'string' && entry.toLowerCase().includes('text/event-stream'));
    }
    return false;
}
/**
 * SSE endpoint paths that must never be compressed by the compression middleware.
 *
 * The compression middleware filter runs before route handlers, so
 * `res.getHeader('Content-Type')` is still undefined at that point.
 * This means the Accept-header check alone is not sufficient for
 * non-standard clients (e.g. curl, fetch) that omit Accept.
 * Path-based exclusion acts as a deterministic fallback.
 */
const SSE_PATH_PREFIXES = [
    '/api/event',
    '/api/global/event',
    '/api/notifications/stream',
    '/api/openchamber/events',
    '/api/openchamber/realtime-proxy/sse',
];
function shouldSkipCompression(req, res) {
    if (process.env.OPENCHAMBER_RUNTIME === 'desktop') {
        return true;
    }
    if (headerIncludesEventStream(req.headers.accept)) {
        return true;
    }
    const pathname = req.path || req.url || '';
    if ((pathname === '/api' || pathname.startsWith('/api/')) && shouldSkipApiCompression()) {
        return true;
    }
    for (const prefix of SSE_PATH_PREFIXES) {
        if (pathname === prefix) {
            return true;
        }
    }
    return headerIncludesEventStream(res.getHeader('Content-Type'));
}
const OPENCHAMBER_VERSION = (() => {
    try {
        const packagePath = path.resolve(__dirname, '..', 'package.json');
        const raw = fs.readFileSync(packagePath, 'utf8');
        const pkg = JSON.parse(raw);
        if (pkg && typeof pkg.version === 'string' && pkg.version.trim().length > 0) {
            return pkg.version.trim();
        }
    }
    catch {
    }
    return 'unknown';
})();
const isEnvFlagEnabled = (value) => {
    if (value === true || value === 1)
        return true;
    if (typeof value !== 'string')
        return false;
    const normalized = value.trim().toLowerCase();
    return normalized === '1' || normalized === 'true';
};
const isEnvFlagDisabled = (value) => {
    if (value === false || value === 0)
        return true;
    if (typeof value !== 'string')
        return false;
    const normalized = value.trim().toLowerCase();
    return normalized === '0' || normalized === 'false';
};
const shouldSkipApiCompression = () => {
    if (isEnvFlagEnabled(process.env.OPENCHAMBER_SKIP_API_COMPRESSION))
        return true;
    if (isEnvFlagEnabled(process.env.OPENCHAMBER_COMPRESS_API))
        return false;
    if (isEnvFlagDisabled(process.env.OPENCHAMBER_COMPRESS_API))
        return true;
    return process.env.OPENCHAMBER_RUNTIME === 'desktop';
};
const OPENCHAMBER_VERBOSE_REQUEST_LOGS = isEnvFlagEnabled(process.env.OPENCHAMBER_VERBOSE_REQUEST_LOGS);
const PLAN_MODE_EXPERIMENT_ENABLED = isEnvFlagEnabled(process.env.OPENCODE_EXPERIMENTAL_PLAN_MODE)
    || isEnvFlagEnabled(process.env.OPENCODE_EXPERIMENTAL);
const fsPromises = fs.promises;
const settingsNormalizationRuntime = createSettingsNormalizationRuntime({
    os,
    path,
    processLike: process,
    realpathSync: fs.realpathSync,
    readdirSync: fs.readdirSync,
    tunnelBootstrapTtlDefaultMs: TUNNEL_BOOTSTRAP_TTL_DEFAULT_MS,
    tunnelBootstrapTtlMinMs: TUNNEL_BOOTSTRAP_TTL_MIN_MS,
    tunnelBootstrapTtlMaxMs: TUNNEL_BOOTSTRAP_TTL_MAX_MS,
    tunnelSessionTtlDefaultMs: TUNNEL_SESSION_TTL_DEFAULT_MS,
    tunnelSessionTtlMinMs: TUNNEL_SESSION_TTL_MIN_MS,
    tunnelSessionTtlMaxMs: TUNNEL_SESSION_TTL_MAX_MS,
});
const normalizeDirectoryPath = (...args) => settingsNormalizationRuntime.normalizeDirectoryPath(...args);
const normalizePathForPersistence = (...args) => settingsNormalizationRuntime.normalizePathForPersistence(...args);
const normalizeSettingsPaths = (...args) => settingsNormalizationRuntime.normalizeSettingsPaths(...args);
const normalizeManagedRemoteTunnelHostname = (...args) => settingsNormalizationRuntime.normalizeManagedRemoteTunnelHostname(...args);
const normalizeManagedRemoteTunnelPresets = (...args) => settingsNormalizationRuntime.normalizeManagedRemoteTunnelPresets(...args);
const normalizeManagedRemoteTunnelPresetTokens = (...args) => settingsNormalizationRuntime.normalizeManagedRemoteTunnelPresetTokens(...args);
const isUnsafeSkillRelativePath = (...args) => settingsNormalizationRuntime.isUnsafeSkillRelativePath(...args);
const normalizeStringArray = (...args) => settingsNormalizationRuntime.normalizeStringArray(...args);
const sanitizeModelRefs = (...args) => settingsNormalizationRuntime.sanitizeModelRefs(...args);
const sanitizeSkillCatalogs = (...args) => settingsNormalizationRuntime.sanitizeSkillCatalogs(...args);
const sanitizeProjects = (...args) => settingsNormalizationRuntime.sanitizeProjects(...args);
// Every OpenChamber-owned file and folder hangs off one root: the default
// `~/.config/openchamber`, or `OPENCHAMBER_DATA_DIR` when set. The user
// folders (`projects/`, `themes/`, `speech-models/`) are copied into a custom
// root once at startup (`migrateLegacyUserDirs`), because they used to ignore
// the variable.
const OPENCHAMBER_DEFAULT_CONFIG_ROOT = path.join(os.homedir(), '.config', 'openchamber');
const OPENCHAMBER_USER_CONFIG_ROOT = process.env.OPENCHAMBER_DATA_DIR
    ? path.resolve(process.env.OPENCHAMBER_DATA_DIR)
    : OPENCHAMBER_DEFAULT_CONFIG_ROOT;
const OPENCHAMBER_USER_THEMES_DIR = path.join(OPENCHAMBER_USER_CONFIG_ROOT, 'themes');
const OPENCHAMBER_PROJECTS_CONFIG_DIR = path.join(OPENCHAMBER_USER_CONFIG_ROOT, 'projects');
// OPENCHAMBER_CHATS_DIR relocates managed chat worktrees — needed when the
// OpenCode server runs as a separate user that cannot traverse $HOME.
const OPENCHAMBER_CHATS_DIR = process.env.OPENCHAMBER_CHATS_DIR && process.env.OPENCHAMBER_CHATS_DIR.trim()
    ? path.resolve(process.env.OPENCHAMBER_CHATS_DIR.trim())
    : path.join(OPENCHAMBER_USER_CONFIG_ROOT, 'chats');
const MAX_THEME_JSON_BYTES = 512 * 1024;
const themeRuntime = createThemeRuntime({
    fsPromises,
    path,
    themesDir: OPENCHAMBER_USER_THEMES_DIR,
    maxThemeJsonBytes: MAX_THEME_JSON_BYTES,
    logger: console,
});
const readCustomThemesFromDisk = (...args) => themeRuntime.readCustomThemesFromDisk(...args);
const saveImportedTheme = (...args) => themeRuntime.saveImportedTheme(...args);
const deleteImportedTheme = (...args) => themeRuntime.deleteImportedTheme(...args);
let notificationTemplateRuntime = null;
let agentToolRuntime = null;
let managedConfigRuntime = null;
const fetchFreeZenModels = (...args) => notificationTemplateRuntime.fetchFreeZenModels(...args);
const maybeCacheSessionInfoFromEvent = (...args) => notificationTemplateRuntime.maybeCacheSessionInfoFromEvent(...args);
const getCachedZenModels = (...args) => notificationTemplateRuntime.getCachedZenModels(...args);
const OPENCHAMBER_DATA_DIR = OPENCHAMBER_USER_CONFIG_ROOT;
const SETTINGS_FILE_PATH = path.join(OPENCHAMBER_DATA_DIR, 'settings.json');
const CLOUDFLARE_MANAGED_REMOTE_TUNNELS_FILE_PATH = path.join(OPENCHAMBER_DATA_DIR, 'cloudflare-managed-remote-tunnels.json');
const CLOUDFLARE_LEGACY_NAMED_TUNNELS_FILE_PATH = path.join(OPENCHAMBER_DATA_DIR, 'cloudflare-named-tunnels.json');
const CLOUDFLARE_MANAGED_REMOTE_TUNNELS_VERSION = 1;
const worktreeBootstrapStore = createWorktreeBootstrapStore({
    filePath: path.join(OPENCHAMBER_DATA_DIR, 'git-worktree-bootstrap.json'),
    fsImpl: fsPromises,
});
const managedTunnelConfigRuntime = createManagedTunnelConfigRuntime({
    fsPromises,
    path,
    normalizeManagedRemoteTunnelHostname,
    normalizeManagedRemoteTunnelPresets,
    constants: {
        CLOUDFLARE_MANAGED_REMOTE_TUNNELS_FILE_PATH,
        CLOUDFLARE_LEGACY_NAMED_TUNNELS_FILE_PATH,
        CLOUDFLARE_MANAGED_REMOTE_TUNNELS_VERSION,
    },
});
const syncManagedRemoteTunnelConfigWithPresets = (...args) => managedTunnelConfigRuntime.syncManagedRemoteTunnelConfigWithPresets(...args);
const upsertManagedRemoteTunnelToken = (...args) => managedTunnelConfigRuntime.upsertManagedRemoteTunnelToken(...args);
const settingsHelpers = createSettingsHelpers({
    normalizePathForPersistence,
    normalizeDirectoryPath,
    normalizeTunnelProvider,
    normalizeTunnelMode,
    normalizeOptionalPath,
    normalizeManagedRemoteTunnelHostname,
    normalizeManagedRemoteTunnelPresets,
    normalizeManagedRemoteTunnelPresetTokens,
    normalizeStringArray,
    sanitizeModelRefs,
    sanitizeSkillCatalogs,
    sanitizeProjects,
});
const normalizePwaAppName = (...args) => settingsHelpers.normalizePwaAppName(...args);
const normalizePwaOrientation = (...args) => settingsHelpers.normalizePwaOrientation(...args);
const sanitizeSettingsUpdate = (...args) => settingsHelpers.sanitizeSettingsUpdate(...args);
const mergePersistedSettings = (...args) => settingsHelpers.mergePersistedSettings(...args);
const formatSettingsResponse = (...args) => settingsHelpers.formatSettingsResponse(...args);
const projectDirectoryRuntime = createProjectDirectoryRuntime({
    fsPromises,
    path,
    normalizeDirectoryPath,
    getReadSettingsFromDiskMigrated: () => readSettingsFromDiskMigrated,
    sanitizeProjects,
});
const resolveDirectoryCandidate = (...args) => projectDirectoryRuntime.resolveDirectoryCandidate(...args);
const validateDirectoryPath = (...args) => projectDirectoryRuntime.validateDirectoryPath(...args);
const resolveProjectDirectory = (...args) => projectDirectoryRuntime.resolveProjectDirectory(...args);
const resolveOptionalProjectDirectory = (...args) => projectDirectoryRuntime.resolveOptionalProjectDirectory(...args);
const settingsRuntime = createSettingsRuntime({
    fsPromises,
    path,
    crypto,
    SETTINGS_FILE_PATH,
    sanitizeProjects,
    sanitizeSettingsUpdate,
    mergePersistedSettings,
    normalizeSettingsPaths,
    normalizeStringArray,
    formatSettingsResponse,
    resolveDirectoryCandidate,
    normalizeManagedRemoteTunnelHostname,
    normalizeManagedRemoteTunnelPresets,
    normalizeManagedRemoteTunnelPresetTokens,
    syncManagedRemoteTunnelConfigWithPresets,
    upsertManagedRemoteTunnelToken,
    onManagedPluginSettingsChanged: () => managedConfigRuntime?.refreshManagedConfigFile(),
    // Declared further down; settings are only saved once the server serves requests.
    onMessageSearchEnabledChanged: (enabled) => messageSearchRuntime.setEnabled(enabled),
    onMessageSearchReasoningChanged: (enabled) => messageSearchRuntime.setReasoningEnabled(enabled),
});
const readSettingsFromDiskMigrated = (...args) => settingsRuntime.readSettingsFromDiskMigrated(...args);
const readSettingsFromDisk = (...args) => settingsRuntime.readSettingsFromDisk(...args);
const writeSettingsToDisk = (...args) => settingsRuntime.writeSettingsToDisk(...args);
const persistSettings = (...args) => settingsRuntime.persistSettings(...args);
const requestSecurityRuntime = createRequestSecurityRuntime({
    readSettingsFromDiskMigrated,
    allowLocalDevOrigins: allowsLocalDevOrigins(process.env),
});
const rejectWebSocketUpgrade = (...args) => requestSecurityRuntime.rejectWebSocketUpgrade(...args);
const isRequestOriginAllowed = (...args) => requestSecurityRuntime.isRequestOriginAllowed(...args);
const notificationEmitterRuntime = createNotificationEmitterRuntime({
    process,
    getDesktopNotifyEnabled: () => ENV_DESKTOP_NOTIFY,
    desktopNotifyPrefix: DESKTOP_NOTIFY_PREFIX,
    getUiNotificationClients: () => uiNotificationClients,
    getOpenChamberEventClients: () => uiOpenChamberEventClients,
    getBroadcastGlobalUiEvent: () => broadcastGlobalUiEvent,
});
const writeSseEvent = (...args) => notificationEmitterRuntime.writeSseEvent(...args);
const broadcastGlobalUiEvent = createGlobalUiEventBroadcaster({
    sseClients: uiNotificationClients,
    wsClients: uiNotificationWsClients,
    writeSseEvent,
});
const broadcastUiNotification = (...args) => notificationEmitterRuntime.broadcastUiNotification(...args);
// OpenChamber-owned events (queue changes, archive state) travel on the shared
// control stream, not on OpenCode's event proxy. One broadcaster so every owner
// reaches the same clients.
const broadcastOpenChamberUiEvent = createGlobalUiEventBroadcaster({
    sseClients: uiOpenChamberEventClients,
    wsClients: uiNotificationWsClients,
    writeSseEvent,
});
/**
 * Per-session OpenChamber state (goal progress, the assist recap, the
 * obligatory-context cursor, pinned notes) lives in OpenCode's session
 * metadata. The store merge-patches it there and migrates what older
 * OpenChamber versions kept in `sessions-metadata.json`.
 */
const sessionMetadataStore = createSessionMetadataStore({
    dataDir: OPENCHAMBER_DATA_DIR,
    // Called, not captured: the OpenCode URL and auth helpers are declared
    // further down and only ever used once a request arrives.
    openCode: {
        read: (...args) => createOpenCodeSessionMetadata({ buildOpenCodeUrl, getOpenCodeAuthHeaders }).read(...args),
        write: (...args) => createOpenCodeSessionMetadata({ buildOpenCodeUrl, getOpenCodeAuthHeaders }).write(...args),
    },
});
const readStoredSessionMetadata = (sessionID) => sessionMetadataStore.get(sessionID);
const persistSessionMetadataPatch = async (sessionID, patch, { directory = '' } = {}) => {
    const metadata = await sessionMetadataStore.setSessionMetadata(sessionID, patch, { directory });
    // The full merged object, so a client that missed an earlier patch does not
    // have to reconstruct it.
    broadcastOpenChamberUiEvent({
        type: 'openchamber:session-metadata',
        properties: { sessionID, metadata },
    });
    return metadata;
};
const sessionRuntime = createSessionRuntime({
    writeSseEvent,
    getNotificationClients: () => uiNotificationClients,
    broadcastEvent: broadcastGlobalUiEvent,
});
const getActiveSessionCount = () => sessionRuntime.getActiveSessionCount();
const getUpstreamStallTimeoutMs = () => (getActiveSessionCount() > 1
    ? UPSTREAM_STALL_TIMEOUT_CONCURRENT_MS
    : DEFAULT_UPSTREAM_STALL_TIMEOUT_MS);
const movedUserDirs = await migrateLegacyUserDirs({
    fsPromises,
    path,
    dataDir: OPENCHAMBER_USER_CONFIG_ROOT,
    legacyRoot: OPENCHAMBER_DEFAULT_CONFIG_ROOT,
    warn: (message) => console.warn(`[data-dir] ${message}`),
});
if (movedUserDirs.length > 0) {
    console.log(`[data-dir] Copied ${movedUserDirs.join(', ')} into ${OPENCHAMBER_USER_CONFIG_ROOT}`);
}
const projectConfigRuntime = createProjectConfigRuntime({
    fsPromises,
    path,
    projectsDirPath: OPENCHAMBER_PROJECTS_CONFIG_DIR,
});
const projectContextRuntime = createProjectContextRuntime({
    fsPromises,
    path,
    projectsDirPath: OPENCHAMBER_PROJECTS_CONFIG_DIR,
    resolveSharedPlansDir: (projectId) => projectConfigRuntime.resolveSharedPlansDir(projectId),
});
const agentMemoryRuntime = createAgentMemoryRuntime({
    fsPromises,
    path,
    projectsDirPath: OPENCHAMBER_PROJECTS_CONFIG_DIR,
    userConfigRoot: OPENCHAMBER_USER_CONFIG_ROOT,
});
/**
 * One switch for everything memory-related. It gates the tool, these routes,
 * and the session index alike, so turning memory off leaves nothing behind
 * that still reads or writes the store.
 */
const isAgentMemoryEnabled = async () => {
    // The feature gate comes first: unreleased means absent, not merely switched
    // off, so no stored setting can bring it back.
    if (!isAgentMemoryFeatureAvailable()) {
        return false;
    }
    const settings = await readSettingsFromDiskMigrated().catch(() => null);
    return settings?.agentMemoryToolEnabled === true;
};
// HMR-persistent state via globalThis
// These values survive Vite HMR reloads to prevent zombie OpenCode processes
const hmrStateRuntime = createHmrStateRuntime({
    globalThisLike: globalThis,
    os,
    processLike: process,
    stateKey: '__openchamberHmrState',
});
const hmrState = hmrStateRuntime.getOrCreateHmrState();
hmrStateRuntime.ensureUserProvidedOpenCodePassword(hmrState);
// Non-HMR state (safe to reset on reload)
let healthCheckInterval = null;
let server = null;
let expressApp = null;
let currentRestartPromise = null;
let isRestartingOpenCode = false;
let openCodeApiPrefix = '';
let openCodeApiPrefixDetected = true;
let openCodeApiDetectionTimer = null;
let lastOpenCodeError = null;
let lastOpenCodeLaunchDiagnostics = null;
let lastOpenCodeHealthFailure = null;
let lastManagedOpenCodeProcess = null;
let lastOpenCodeRestartDiagnostics = null;
let isOpenCodeReady = false;
let openCodeNotReadySince = 0;
let isExternalOpenCode = false;
let exitOnShutdown = true;
let uiAuthController = null;
let globalWatcherStartPromise = null;
let terminalRuntime = null;
let dictationRuntime = null;
let messageStreamRuntime = null;
const userProvidedOpenCodePassword = hmrStateRuntime.getUserProvidedOpenCodePassword(hmrState);
const initialOpenCodeAuthState = hmrStateRuntime.resolveOpenCodeAuthFromState({
    hmrState,
    userProvidedOpenCodePassword,
});
let openCodeAuthPassword = initialOpenCodeAuthState.openCodeAuthPassword;
let openCodeAuthSource = initialOpenCodeAuthState.openCodeAuthSource;
// Sync helper - call after modifying any HMR state variable
const syncToHmrState = () => {
    hmrStateRuntime.syncStateFromRuntime(hmrState, {
        openCodeProcess,
        openCodePort,
        openCodeBaseUrl,
        isShuttingDown,
        signalsAttached,
        openCodeWorkingDirectory,
        openCodeAuthPassword,
        openCodeAuthSource,
    });
};
// Sync helper - call to restore state from HMR (e.g., on module reload)
const syncFromHmrState = () => {
    const restored = hmrStateRuntime.restoreRuntimeFromState({
        hmrState,
        userProvidedOpenCodePassword,
    });
    openCodeProcess = restored.openCodeProcess;
    openCodePort = restored.openCodePort;
    openCodeBaseUrl = restored.openCodeBaseUrl;
    isShuttingDown = restored.isShuttingDown;
    signalsAttached = restored.signalsAttached;
    openCodeWorkingDirectory = restored.openCodeWorkingDirectory;
    openCodeAuthPassword = restored.openCodeAuthPassword;
    openCodeAuthSource = restored.openCodeAuthSource;
};
// Module-level variables that shadow HMR state
// These are synced to/from hmrState to survive HMR reloads
let openCodeProcess = hmrState.openCodeProcess;
let openCodePort = hmrState.openCodePort;
let openCodeBaseUrl = hmrState.openCodeBaseUrl ?? null;
let isShuttingDown = hmrState.isShuttingDown;
let signalsAttached = hmrState.signalsAttached;
let openCodeWorkingDirectory = hmrState.openCodeWorkingDirectory;
const { configuredOpenCodePort: ENV_CONFIGURED_OPENCODE_PORT, configuredOpenCodeHost: ENV_CONFIGURED_OPENCODE_HOST, effectivePort: ENV_EFFECTIVE_PORT, configuredOpenCodeHostname: ENV_CONFIGURED_OPENCODE_HOSTNAME, } = resolveOpenCodeEnvConfig({
    env: process.env,
    logger: console,
});
const ENV_SKIP_OPENCODE_START = process.env.OPENCODE_SKIP_START === 'true' ||
    process.env.OPENCHAMBER_SKIP_OPENCODE_START === 'true';
const ENV_DESKTOP_NOTIFY = (() => {
    if (process.env.OPENCHAMBER_DESKTOP_NOTIFY === 'true') {
        return true;
    }
    if (process.env.OPENCHAMBER_RUNTIME === 'desktop') {
        return true;
    }
    const argv0 = typeof process.argv?.[0] === 'string' ? process.argv[0] : '';
    const argv1 = typeof process.argv?.[1] === 'string' ? process.argv[1] : '';
    return /openchamber-server/i.test(argv0) || /openchamber-server/i.test(argv1);
})();
const openCodeAuthStateRuntime = createOpenCodeAuthStateRuntime({
    crypto,
    process,
    getAuthPassword: () => openCodeAuthPassword,
    setAuthPassword: (value) => {
        openCodeAuthPassword = value;
    },
    getAuthSource: () => openCodeAuthSource,
    setAuthSource: (value) => {
        openCodeAuthSource = value;
    },
    getUserProvidedPassword: () => userProvidedOpenCodePassword,
    syncToHmrState,
});
const getOpenCodeAuthHeaders = (...args) => openCodeAuthStateRuntime.getOpenCodeAuthHeaders(...args);
const isOpenCodeConnectionSecure = (...args) => openCodeAuthStateRuntime.isOpenCodeConnectionSecure(...args);
const ensureLocalOpenCodeServerPassword = (...args) => openCodeAuthStateRuntime.ensureLocalOpenCodeServerPassword(...args);
const openCodeNetworkState = {};
Object.defineProperties(openCodeNetworkState, {
    openCodePort: { get: () => openCodePort, set: (value) => { openCodePort = value; } },
    openCodeBaseUrl: { get: () => openCodeBaseUrl, set: (value) => { openCodeBaseUrl = value; } },
    openCodeApiPrefix: { get: () => openCodeApiPrefix, set: (value) => { openCodeApiPrefix = value; } },
    openCodeApiPrefixDetected: { get: () => openCodeApiPrefixDetected, set: (value) => { openCodeApiPrefixDetected = value; } },
    openCodeApiDetectionTimer: { get: () => openCodeApiDetectionTimer, set: (value) => { openCodeApiDetectionTimer = value; } },
});
const openCodeNetworkRuntime = createOpenCodeNetworkRuntime({
    state: openCodeNetworkState,
    getOpenCodeAuthHeaders,
    configuredOpenCodeHostname: ENV_CONFIGURED_OPENCODE_HOSTNAME,
});
const waitForReady = (...args) => openCodeNetworkRuntime.waitForReady(...args);
const normalizeApiPrefix = (...args) => openCodeNetworkRuntime.normalizeApiPrefix(...args);
const setDetectedOpenCodeApiPrefix = (...args) => openCodeNetworkRuntime.setDetectedOpenCodeApiPrefix(...args);
const buildOpenCodeUrl = (...args) => openCodeNetworkRuntime.buildOpenCodeUrl(...args);
const ensureOpenCodeApiPrefix = (...args) => openCodeNetworkRuntime.ensureOpenCodeApiPrefix(...args);
const scheduleOpenCodeApiDetection = (...args) => openCodeNetworkRuntime.scheduleOpenCodeApiDetection(...args);
// Plugin-registered providers exist only inside the running OpenCode process.
// Small-model callers resolve them through this connection; without it they
// stay on the file-based resolution and plugin models remain unreachable.
configureOpenCodeRuntimeProviders({
    buildOpenCodeUrl,
    getOpenCodeAuthHeaders,
    getDefaultDirectory: () => openCodeLifecycleRuntime.getDefaultOpenCodeDirectory(),
});
const ENV_CONFIGURED_API_PREFIX = normalizeApiPrefix(process.env.OPENCODE_API_PREFIX || process.env.OPENCHAMBER_API_PREFIX || '');
if (ENV_CONFIGURED_API_PREFIX && ENV_CONFIGURED_API_PREFIX !== '') {
    console.warn('Ignoring configured OpenCode API prefix; API runs at root.');
}
let cachedLoginShellEnvSnapshot;
let resolvedOpencodeBinary = null;
let resolvedOpencodeBinarySource = null;
let resolvedNodeBinary = null;
let resolvedBunBinary = null;
let resolvedGitBinary = null;
let useWslForOpencode = false;
let resolvedWslBinary = null;
let resolvedWslOpencodePath = null;
let resolvedWslDistro = null;
const openCodeEnvState = {};
Object.defineProperties(openCodeEnvState, {
    cachedLoginShellEnvSnapshot: { get: () => cachedLoginShellEnvSnapshot, set: (value) => { cachedLoginShellEnvSnapshot = value; } },
    resolvedOpencodeBinary: { get: () => resolvedOpencodeBinary, set: (value) => { resolvedOpencodeBinary = value; } },
    resolvedOpencodeBinarySource: { get: () => resolvedOpencodeBinarySource, set: (value) => { resolvedOpencodeBinarySource = value; } },
    resolvedNodeBinary: { get: () => resolvedNodeBinary, set: (value) => { resolvedNodeBinary = value; } },
    resolvedBunBinary: { get: () => resolvedBunBinary, set: (value) => { resolvedBunBinary = value; } },
    resolvedGitBinary: { get: () => resolvedGitBinary, set: (value) => { resolvedGitBinary = value; } },
    useWslForOpencode: { get: () => useWslForOpencode, set: (value) => { useWslForOpencode = value; } },
    resolvedWslBinary: { get: () => resolvedWslBinary, set: (value) => { resolvedWslBinary = value; } },
    resolvedWslOpencodePath: { get: () => resolvedWslOpencodePath, set: (value) => { resolvedWslOpencodePath = value; } },
    resolvedWslDistro: { get: () => resolvedWslDistro, set: (value) => { resolvedWslDistro = value; } },
});
const openCodeEnvRuntime = createOpenCodeEnvRuntime({
    state: openCodeEnvState,
    normalizeDirectoryPath,
    readSettingsFromDiskMigrated,
    providedLoginShellEnvSnapshot,
});
const applyLoginShellEnvSnapshot = (...args) => openCodeEnvRuntime.applyLoginShellEnvSnapshot(...args);
const getLoginShellEnvSnapshot = (...args) => openCodeEnvRuntime.getLoginShellEnvSnapshot(...args);
const ensureOpencodeCliEnv = (...args) => openCodeEnvRuntime.ensureOpencodeCliEnv(...args);
const applyOpencodeBinaryFromSettings = (...args) => openCodeEnvRuntime.applyOpencodeBinaryFromSettings(...args);
const resolveOpencodeCliPath = (...args) => openCodeEnvRuntime.resolveOpencodeCliPath(...args);
const isBundledOpenCodeCliPath = (...args) => openCodeEnvRuntime.isBundledOpenCodeCliPath(...args);
const isExecutable = (...args) => openCodeEnvRuntime.isExecutable(...args);
const searchPathFor = (...args) => openCodeEnvRuntime.searchPathFor(...args);
const resolveGitBinaryForSpawn = (...args) => openCodeEnvRuntime.resolveGitBinaryForSpawn(...args);
const resolveManagedOpenCodeLaunchSpec = (...args) => openCodeEnvRuntime.resolveManagedOpenCodeLaunchSpec(...args);
const clearResolvedOpenCodeBinary = (...args) => openCodeEnvRuntime.clearResolvedOpenCodeBinary(...args);
const openCodeResolutionRuntime = createOpenCodeResolutionRuntime({
    path,
    resolveOpencodeCliPath,
    applyOpencodeBinaryFromSettings,
    ensureOpencodeCliEnv,
    resolveManagedOpenCodeLaunchSpec,
    getResolvedState: () => ({
        resolvedOpencodeBinary,
        resolvedOpencodeBinarySource,
        useWslForOpencode,
        resolvedWslBinary,
        resolvedWslOpencodePath,
        resolvedWslDistro,
        resolvedNodeBinary,
        resolvedBunBinary,
    }),
    setResolvedOpencodeBinarySource: (value) => {
        resolvedOpencodeBinarySource = value;
    },
});
const getOpenCodeResolutionSnapshot = (...args) => openCodeResolutionRuntime.getOpenCodeResolutionSnapshot(...args);
applyLoginShellEnvSnapshot();
notificationTemplateRuntime = createNotificationTemplateRuntime({
    readSettingsFromDisk,
    persistSettings,
    buildOpenCodeUrl,
    getOpenCodeAuthHeaders,
    resolveGitBinaryForSpawn,
});
// Which sessions are subsessions: per-turn work skips them without reading.
const sessionLineage = createSessionLineage();
/**
 * Owns what a session must be told about the project's knowledge. Every sender
 * asks it — the UI over HTTP, scheduled tasks and agent-dispatched sessions in
 * process — so the answer cannot differ between them.
 */
const sessionKnowledgeRuntime = createSessionKnowledgeRuntime({
    projectContextRuntime,
    agentMemoryRuntime,
    // Called, not captured: the resolver is declared further down, and taking a
    // reference here would read it before it exists.
    resolveProjectId: (directory) => resolveMemoryProjectId(directory),
    isAgentMemoryEnabled,
    // The plugin carrying the tool exists only in an OpenCode we launched.
    isSessionLinkingAvailable: async () => {
        if (isExternalOpenCode || ENV_SKIP_OPENCODE_START)
            return false;
        const settings = await readSettingsFromDiskMigrated().catch(() => null);
        return settings?.agentControlToolEnabled !== false;
    },
    readSessionMetadata: readStoredSessionMetadata,
    // Pins and the delivered-signature cursor are read from and written to
    // OpenChamber's own store; nothing here talks to OpenCode any more.
    persistSessionMetadata: (sessionID, directory, patch) => persistSessionMetadataPatch(sessionID, patch, { directory }),
});
const contextObligatoryRuntime = createContextObligatoryRuntime({
    buildOpenCodeUrl,
    getOpenCodeAuthHeaders,
    sessionKnowledgeRuntime,
    readSessionMetadata: readStoredSessionMetadata,
    persistContextCursor: (sessionID, directory, patch) => persistSessionMetadataPatch(sessionID, patch, { directory }),
});
const globalMessageStreamHub = createGlobalMessageStreamHub({
    buildOpenCodeUrl,
    getOpenCodeAuthHeaders,
    upstreamStallTimeoutMs: getUpstreamStallTimeoutMs,
    deltaCoalesceWindowMs: resolveDeltaCoalesceWindowMs(),
});
// Queued follow-up messages are delivered by the server so a closed tab or a
// dropped connection no longer strands them (VS Code keeps its UI-side queue).
const messageQueueRuntime = createMessageQueueRuntime({
    globalEventHub: globalMessageStreamHub,
    buildOpenCodeUrl,
    getOpenCodeAuthHeaders,
    sessionKnowledgeRuntime,
    // OpenCode's event SSE proxy cannot carry OpenChamber-owned events. Use the
    // shared control stream for SSE clients and the existing WS fan-out.
    broadcastGlobalUiEvent: broadcastOpenChamberUiEvent,
    onPromptSent: (sessionId) => sessionRuntime.markUserMessageSent(sessionId),
    dataDir: OPENCHAMBER_DATA_DIR,
});
messageQueueRuntime.start();
// Full-text search over this server's conversations (user messages and agent
// replies). Opt-in: off by default, and off means idle. The index is derived
// data in the data dir, fed from the same event stream; see lib/message-search.
const messageSearchRuntime = createMessageSearchRuntime({
    dataDir: OPENCHAMBER_DATA_DIR,
    buildOpenCodeUrl,
    getOpenCodeAuthHeaders,
    globalEventHub: globalMessageStreamHub,
    readSettings: async () => {
        const settings = await readSettingsFromDisk();
        return { enabled: settings.messageSearchEnabled === true, reasoning: settings.messageSearchReasoningEnabled === true };
    },
});
const openCodeWatcherRuntime = createOpenCodeWatcherRuntime({
    waitForOpenCodePort: (...args) => waitForOpenCodePort(...args),
    buildOpenCodeUrl,
    getOpenCodeAuthHeaders,
    parseSseDataPayload: (...args) => parseSseDataPayload(...args),
    globalEventHub: globalMessageStreamHub,
    onPayload: (payload) => {
        maybeCacheSessionInfoFromEvent(payload);
        sessionRuntime.processOpenCodeSsePayload(payload);
    },
});
globalMessageStreamHub.subscribeEvent((event) => {
    const directory = typeof event?.directory === 'string' && event.directory && event.directory !== 'global'
        ? event.directory
        : '';
    // The hub translates v2 wire events into the server's vocabulary once; these
    // consumers were written against it and stay unaware of the wire shape.
    for (const payload of event?.translated?.() ?? []) {
        // The user cancelled the turn: stop the OpenChamber tool actions that
        // turn still has running (a browser action, a memory write). OpenCode 2
        // gives the plugin no abort signal, so this is where the cancel lands.
        if (payload.type === 'session.idle' && payload.properties?.aborted === true) {
            agentToolRuntime?.abortSession?.(payload.properties.sessionID);
        }
        sessionLineage.observe(payload);
        contextObligatoryRuntime.processPayload(payload, directory || payload.properties?.directory || '');
    }
});
/**
 * Browser bridges hand us the raw v2 wire payload (they forward it untouched to
 * the client). The synthetic OpenChamber frames the UI also expects are derived
 * from the translated event, so the translation happens here.
 */
const processForwardedEventPayload = (payload, emitSyntheticEvent) => {
    if (!payload || typeof payload !== 'object' || typeof emitSyntheticEvent !== 'function') {
        return;
    }
    for (const translated of translateWireEvent(payload)) {
        processForwardedServerEvent(translated, emitSyntheticEvent);
    }
};
const processForwardedServerEvent = (payload, emitSyntheticEvent) => {
    maybeCacheSessionInfoFromEvent(payload);
    if (payload.type !== 'session.status') {
        return;
    }
    const properties = payload.properties && typeof payload.properties === 'object' ? payload.properties : {};
    const statusInfo = properties.status && typeof properties.status === 'object' ? properties.status : {};
    const info = properties.info && typeof properties.info === 'object' ? properties.info : {};
    const sessionId = typeof properties.sessionID === 'string' ? properties.sessionID.trim() : '';
    const status = typeof statusInfo.type === 'string'
        ? statusInfo.type.trim()
        : (typeof info.type === 'string' ? info.type.trim() : '');
    if (!sessionId || !status) {
        return;
    }
    emitSyntheticEvent({
        type: 'openchamber:session-status',
        properties: {
            sessionID: sessionId,
            status,
            timestamp: Date.now(),
            metadata: {
                attempt: typeof statusInfo.attempt === 'number'
                    ? statusInfo.attempt
                    : (typeof info.attempt === 'number' ? info.attempt : undefined),
                message: typeof statusInfo.message === 'string'
                    ? statusInfo.message
                    : (typeof info.message === 'string' ? info.message : undefined),
                next: typeof statusInfo.next === 'number'
                    ? statusInfo.next
                    : (typeof info.next === 'number' ? info.next : undefined),
            },
            needsAttention: false,
        },
    });
    emitSyntheticEvent({
        type: 'openchamber:session-activity',
        properties: {
            sessionId,
            phase: status === 'busy' || status === 'retry' ? 'busy' : 'idle',
        },
    });
};
const serverUtilsRuntime = createServerUtilsRuntime({
    // Read lazily: the archive store is created with the session service further
    // down, while the proxy is registered later still.
    getArchivedSessions: () => openChamberSessionService.archiveStore.getAll(),
    getStoredSessionMetadata: () => sessionMetadataStore.listUnmigrated(),
    fs,
    os,
    path,
    process,
    openCodeReadyGraceMs: OPEN_CODE_READY_GRACE_MS,
    longRequestTimeoutMs: LONG_REQUEST_TIMEOUT_MS,
    getRuntime: () => ({
        openCodePort,
        openCodeBaseUrl,
        openCodeNotReadySince,
        isOpenCodeReady,
        isRestartingOpenCode,
    }),
    getOpenCodeAuthHeaders,
    buildOpenCodeUrl,
    ensureOpenCodeApiPrefix,
    getUpstreamStallTimeoutMs,
    getUiNotificationClients: () => uiNotificationClients,
    getOpenCodePort: () => openCodePort,
    setOpenCodePortState: (value) => {
        openCodePort = value;
    },
    syncToHmrState,
    markOpenCodeNotReady: () => {
        isOpenCodeReady = false;
    },
    setOpenCodeNotReadySince: (value) => {
        openCodeNotReadySince = value;
    },
    clearLastOpenCodeError: () => {
        lastOpenCodeError = null;
    },
    getLoginShellPath: () => {
        const snapshot = getLoginShellEnvSnapshot();
        if (!snapshot || typeof snapshot.PATH !== 'string' || snapshot.PATH.length === 0) {
            return null;
        }
        return snapshot.PATH;
    },
});
const setOpenCodePort = (...args) => serverUtilsRuntime.setOpenCodePort(...args);
const waitForOpenCodePort = (...args) => serverUtilsRuntime.waitForOpenCodePort(...args);
const buildAugmentedPath = (...args) => serverUtilsRuntime.buildAugmentedPath(...args);
const buildManagedOpenCodePath = (...args) => serverUtilsRuntime.buildManagedOpenCodePath(...args);
const parseSseDataPayload = (...args) => serverUtilsRuntime.parseSseDataPayload(...args);
const staticRoutesRuntime = createStaticRoutesRuntime({
    fs,
    path,
    process,
    __dirname,
    express,
    resolveProjectDirectory,
    buildOpenCodeUrl,
    getOpenCodeAuthHeaders,
    readSettingsFromDiskMigrated,
    normalizePwaAppName,
    normalizePwaOrientation,
    // uiAuthController is created at startup, after this runtime: read it per request.
    isRequestAuthorized: (req, res) => isRequestAuthorized(req, res, { uiAuthController }),
});
const featureRoutesRuntime = createFeatureRoutesRuntime({
    clientReloadDelayMs: CLIENT_RELOAD_DELAY_MS,
});
const bootstrapRuntime = createBootstrapRuntime({
    createUiAuth,
    registerServerStatusRoutes,
    registerCommonRequestMiddleware,
    registerAuthAndAccessRoutes,
    registerNotificationRoutes,
    registerOpenChamberRoutes,
    express,
});
const startupPipelineRuntime = createStartupPipelineRuntime({
    createMessageStreamWsRuntime,
    createServerStartupRuntime,
});
const openCodeLifecycleState = {};
Object.defineProperties(openCodeLifecycleState, {
    openCodeProcess: { get: () => openCodeProcess, set: (value) => { openCodeProcess = value; } },
    openCodePort: { get: () => openCodePort, set: (value) => { openCodePort = value; } },
    openCodeBaseUrl: { get: () => openCodeBaseUrl, set: (value) => { openCodeBaseUrl = value; } },
    openCodeWorkingDirectory: { get: () => openCodeWorkingDirectory, set: (value) => { openCodeWorkingDirectory = value; } },
    currentRestartPromise: { get: () => currentRestartPromise, set: (value) => { currentRestartPromise = value; } },
    isRestartingOpenCode: { get: () => isRestartingOpenCode, set: (value) => { isRestartingOpenCode = value; } },
    openCodeApiPrefix: { get: () => openCodeApiPrefix, set: (value) => { openCodeApiPrefix = value; } },
    openCodeApiPrefixDetected: { get: () => openCodeApiPrefixDetected, set: (value) => { openCodeApiPrefixDetected = value; } },
    openCodeApiDetectionTimer: { get: () => openCodeApiDetectionTimer, set: (value) => { openCodeApiDetectionTimer = value; } },
    lastOpenCodeError: { get: () => lastOpenCodeError, set: (value) => { lastOpenCodeError = value; } },
    lastOpenCodeLaunchDiagnostics: { get: () => lastOpenCodeLaunchDiagnostics, set: (value) => { lastOpenCodeLaunchDiagnostics = value; } },
    lastOpenCodeHealthFailure: { get: () => lastOpenCodeHealthFailure, set: (value) => { lastOpenCodeHealthFailure = value; } },
    lastManagedOpenCodeProcess: { get: () => lastManagedOpenCodeProcess, set: (value) => { lastManagedOpenCodeProcess = value; } },
    lastOpenCodeRestartDiagnostics: { get: () => lastOpenCodeRestartDiagnostics, set: (value) => { lastOpenCodeRestartDiagnostics = value; } },
    isOpenCodeReady: { get: () => isOpenCodeReady, set: (value) => { isOpenCodeReady = value; } },
    openCodeNotReadySince: { get: () => openCodeNotReadySince, set: (value) => { openCodeNotReadySince = value; } },
    isExternalOpenCode: { get: () => isExternalOpenCode, set: (value) => { isExternalOpenCode = value; } },
    isShuttingDown: { get: () => isShuttingDown, set: (value) => { isShuttingDown = value; } },
    healthCheckInterval: { get: () => healthCheckInterval, set: (value) => { healthCheckInterval = value; } },
    expressApp: { get: () => expressApp, set: (value) => { expressApp = value; } },
    useWslForOpencode: { get: () => useWslForOpencode, set: (value) => { useWslForOpencode = value; } },
    resolvedWslBinary: { get: () => resolvedWslBinary, set: (value) => { resolvedWslBinary = value; } },
    resolvedWslOpencodePath: { get: () => resolvedWslOpencodePath, set: (value) => { resolvedWslOpencodePath = value; } },
    resolvedWslDistro: { get: () => resolvedWslDistro, set: (value) => { resolvedWslDistro = value; } },
});
const openCodeLifecycleRuntime = createOpenCodeLifecycleRuntime({
    state: openCodeLifecycleState,
    env: {
        ENV_CONFIGURED_OPENCODE_PORT,
        ENV_CONFIGURED_OPENCODE_HOST,
        ENV_EFFECTIVE_PORT,
        ENV_CONFIGURED_OPENCODE_HOSTNAME,
        ENV_SKIP_OPENCODE_START,
    },
    syncToHmrState,
    syncFromHmrState,
    getOpenCodeAuthHeaders,
    buildOpenCodeUrl,
    waitForReady,
    normalizeApiPrefix,
    applyOpencodeBinaryFromSettings,
    ensureOpencodeCliEnv,
    ensureLocalOpenCodeServerPassword,
    resolveManagedOpenCodeLaunchSpec,
    setOpenCodePort,
    setDetectedOpenCodeApiPrefix,
    setupProxy: (...args) => setupProxy(...args),
    ensureOpenCodeApiPrefix,
    clearResolvedOpenCodeBinary,
    buildAugmentedPath,
    buildManagedOpenCodePath,
    getManagedOpenCodeShellEnvSnapshot: getLoginShellEnvSnapshot,
    getActiveSessionCount,
    // Most-recently-used directories first: OpenCode initializes each directory
    // lazily on first request (seconds on large session stores), so the
    // lifecycle warms these right after readiness — before the UI's first
    // interactive request would otherwise pay that cost.
    getWarmupDirectories: async () => {
        const settings = await readSettingsFromDiskMigrated().catch(() => null);
        if (!settings)
            return [];
        const directories = [];
        if (typeof settings.lastDirectory === 'string' && settings.lastDirectory) {
            directories.push(settings.lastDirectory);
        }
        const projects = Array.isArray(settings.projects) ? [...settings.projects] : [];
        projects.sort((a, b) => (b?.lastOpenedAt ?? 0) - (a?.lastOpenedAt ?? 0));
        for (const project of projects) {
            if (typeof project?.path === 'string' && project.path) {
                directories.push(project.path);
            }
        }
        // A deleted project would fail every read scoped to it, and the first entry
        // also scopes server-side reads that have no directory of their own.
        const existing = await Promise.all([...new Set(directories)].map(async (directory) => ((await fs.promises.stat(directory).catch(() => null))?.isDirectory() ? directory : null)));
        return existing.filter(Boolean);
    },
    // A managed restart can move OpenCode to a NEW port (the old one may stay
    // occupied if killProcessOnPort/waitForPortRelease didn't free it in time,
    // on any platform). Rebind the message-stream upstream readers to the current port
    // so the UI keeps receiving events instead of staying pinned to the old
    // process (#2638). The runtime is created later by the startup pipeline;
    // by the time any restart runs, it is assigned.
    onOpenCodeRestarted: () => {
        // A restart reloads plugins: provider ports, credentials and the provider
        // list itself can all differ from what was cached.
        resetOpenCodeRuntimeProviders();
        try {
            messageStreamRuntime?.rebindUpstream();
        }
        catch (error) {
            console.warn('Failed to rebind message stream after OpenCode restart:', error?.message ?? error);
        }
        try {
            const { sessionIds } = sessionRuntime.interruptBusySessionsAfterRestart();
            if (sessionIds.length > 0) {
                const multiple = sessionIds.length > 1;
                broadcastUiNotification({
                    title: multiple ? 'Chats interrupted' : 'Chat interrupted',
                    body: multiple
                        ? 'OpenCode restarted during running responses. Send a message in each chat to continue.'
                        : 'OpenCode restarted during a running response. Send a message to continue.',
                    tag: 'opencode-restart-interrupted',
                    kind: 'opencode-restart-interrupted',
                    sessionId: sessionIds[0],
                });
            }
        }
        catch (error) {
            console.warn('Failed to reconcile sessions after OpenCode restart:', error?.message ?? error);
        }
    },
    getManagedOpenCodeEnv: async () => (managedConfigRuntime ? managedConfigRuntime.buildManagedChildEnv() : {}),
});
// Quota lookups, voice keys and routing read provider credentials from the
// running OpenCode (`GET /api/credential`), plus the values of the variables a
// managed OpenCode takes keys from, read from the environment it was given.
configureOpenCodeCredentials(openCodeCredentialSource({
    buildOpenCodeUrl,
    getOpenCodeAuthHeaders,
    getLaunchEnvironment: () => openCodeLifecycleRuntime.getManagedOpenCodeProcessEnv(),
    getDefaultDirectory: () => openCodeLifecycleRuntime.getDefaultOpenCodeDirectory(),
}));
const getOpenCodeCompatibility = async () => {
    if (isExternalOpenCode || ENV_SKIP_OPENCODE_START) {
        const base = ENV_CONFIGURED_OPENCODE_HOST?.origin || openCodeBaseUrl || `http://127.0.0.1:${openCodePort || ENV_EFFECTIVE_PORT}`;
        const version = await readExternalOpenCodeVersion(base, getOpenCodeAuthHeaders()).catch(() => null);
        return describeOpenCodeCompatibility(version, 'external', false);
    }
    const binary = ensureOpencodeCliEnv();
    const installation = isBundledOpenCodeCliPath(binary) ? 'bundled' : 'managed';
    const version = await readOpenCodeCliVersion(resolveManagedOpenCodeLaunchSpec(binary)).catch(() => null);
    // A CLI pinned by the administrator is theirs to replace, never ours.
    const pinnedByPolicy = Boolean(readEnterprisePolicy().opencodeBinary);
    return describeOpenCodeCompatibility(version, installation, supportsOpenCodeV2Install() && !pinnedByPolicy, binary || null);
};
const getOpenCodeUpgradeCapability = () => {
    const activeBinary = lastOpenCodeLaunchDiagnostics?.sourceBinary
        || lastOpenCodeLaunchDiagnostics?.binary
        || resolvedOpencodeBinary;
    return resolveOpenCodeUpgradeCapability({
        isExternal: isExternalOpenCode,
        hasManagedProcess: Boolean(openCodeProcess),
        activeBinary,
        isBundledBinary: isBundledOpenCodeCliPath,
        pinnedByPolicy: Boolean(readEnterprisePolicy().opencodeBinary),
    });
};
const restartOpenCode = (...args) => openCodeLifecycleRuntime.restartOpenCode(...args);
const waitForOpenCodeReady = (...args) => openCodeLifecycleRuntime.waitForOpenCodeReady(...args);
const refreshOpenCodeAfterConfigChange = (...args) => openCodeLifecycleRuntime.refreshOpenCodeAfterConfigChange(...args);
const startHealthMonitoring = () => openCodeLifecycleRuntime.startHealthMonitoring(HEALTH_CHECK_INTERVAL);
const triggerHealthCheck = () => openCodeLifecycleRuntime.triggerHealthCheck();
const emitSessionCreatedEvent = (event) => {
    for (const client of uiOpenChamberEventClients) {
        try {
            writeSseEvent(client, {
                type: 'openchamber:session-created',
                properties: {
                    sessionId: event.sessionID,
                    directory: event.directory,
                    createdAt: event.createdAt,
                    promptDispatched: event.promptDispatched === true,
                    dispatchedAsCommand: event.dispatchedAsCommand === true,
                    ...(event.projectID ? { projectId: event.projectID } : {}),
                    ...(event.title ? { title: event.title } : {}),
                },
            });
        }
        catch {
            uiOpenChamberEventClients.delete(client);
        }
    }
};
/**
 * Maps a session directory onto the project whose memory it belongs to, so a
 * session running in a worktree writes to the project the panel shows.
 */
const resolveMemoryProjectId = createMemoryProjectResolver({
    listProjectPaths: async () => {
        const settings = await readSettingsFromDiskMigrated().catch(() => null);
        return sanitizeProjects(settings?.projects || []).map((project) => project.path);
    },
    resolvePrimaryWorktreeRoot,
    managedProjectRoots: [...new Set([path.join(OPENCHAMBER_USER_CONFIG_ROOT, 'chats'), OPENCHAMBER_CHATS_DIR])],
});
const openChamberSessionService = createOpenChamberSessionService({
    readSettingsFromDiskMigrated,
    sanitizeProjects,
    validateDirectoryPath,
    buildOpenCodeUrl,
    getOpenCodeAuthHeaders,
    waitForOpenCodeReady,
    emitSessionCreatedEvent,
    sessionKnowledgeRuntime,
    worktreeBootstrapStore,
    // TwinCAT project chats never hydrate or create Git worktrees.
    // OpenCode 2.x has no archive route, so the state is OpenChamber's own and
    // lives beside the instance it describes.
    dataDir: OPENCHAMBER_DATA_DIR,
    sessionMetadataStore,
    persistSessionMetadata: persistSessionMetadataPatch,
    broadcastGlobalUiEvent: broadcastOpenChamberUiEvent,
});
// Browser actions are published to whichever OpenChamber clients are connected;
// the one owning the browser panel answers. `emitRequest` returns the number of
// clients reached so the broker can fail fast when nobody is listening.
/**
 * Tells every client the selected browser provider was dropped back to the
 * in-app browser, so Settings shows the change and the user hears why.
 */
// Every browser action passes through here: the in-app view by default, or an
// extension service chosen in Settings → OpenChamber Tools.
// "Show this file" reaches every connected client; the ones showing that
// project open it. Nothing comes back, so the count of clients reached is the
// only signal the agent gets.
const ensureGlobalWatcherStarted = async () => {
    if (globalWatcherStartPromise) {
        return globalWatcherStartPromise;
    }
    globalWatcherStartPromise = openCodeWatcherRuntime.start().catch((error) => {
        globalWatcherStartPromise = null;
        throw error;
    });
    return globalWatcherStartPromise;
};
const bootstrapOpenCodeAtStartup = async (...args) => {
    await openCodeLifecycleRuntime.bootstrapOpenCodeAtStartup(...args);
    scheduleOpenCodeApiDetection();
    if (openCodeLifecycleState.openCodeProcess && !openCodeLifecycleState.isExternalOpenCode) {
        startHealthMonitoring();
    }
    // The global watcher used to start only for desktop notifications; the
    // session-assist runtime also rides its event hub, so it now starts
    // unconditionally once OpenCode is up.
    void ensureGlobalWatcherStarted().catch((error) => {
        console.warn(`Global event watcher startup failed: ${error?.message || error}`);
    });
    // Entries the sweep cannot push now stay in the legacy file and are pushed on
    // the session's next write or the next start.
    void waitForOpenCodeReady()
        .then(() => sessionMetadataStore.migrateLegacy())
        .catch((error) => console.warn('[openchamber-sessions] session metadata migration failed:', error?.message ?? error));
};
const killProcessOnPort = (...args) => openCodeLifecycleRuntime.killProcessOnPort(...args);
const waitForPortRelease = (...args) => openCodeLifecycleRuntime.waitForPortRelease(...args);
const setupProxy = (...args) => serverUtilsRuntime.setupProxy(...args);
const gracefulShutdownRuntime = createGracefulShutdownRuntime({
    process,
    shutdownTimeoutMs: SHUTDOWN_TIMEOUT,
    getExitOnShutdown: () => exitOnShutdown,
    getIsShuttingDown: () => isShuttingDown,
    setIsShuttingDown: (value) => {
        isShuttingDown = value;
    },
    syncToHmrState,
    openCodeWatcherRuntime,
    contextObligatoryRuntime,
    messageQueueRuntime,
    messageSearchRuntime,
    sessionRuntime,
    getHealthCheckInterval: () => healthCheckInterval,
    clearHealthCheckInterval: (value) => clearInterval(value),
    getMessageStreamRuntime: () => messageStreamRuntime,
    setMessageStreamRuntime: (value) => {
        messageStreamRuntime = value;
    },
    shouldSkipOpenCodeStop: () => ENV_SKIP_OPENCODE_START || isExternalOpenCode,
    getOpenCodePort: () => openCodePort,
    getOpenCodeProcess: () => openCodeProcess,
    setOpenCodeProcess: (value) => {
        openCodeProcess = value;
    },
    killProcessOnPort,
    waitForPortRelease,
    getServer: () => server,
    getUiAuthController: () => uiAuthController,
    setUiAuthController: (value) => {
        uiAuthController = value;
    },
});
const gracefulShutdown = (...args) => gracefulShutdownRuntime.gracefulShutdown(...args);
async function main(options = {}) {
    const localServerId = crypto.randomUUID();
    const getActivePort = () => { const address = server?.address(); return address && typeof address === 'object' ? address.port : null; };
    const port = Number.isFinite(options.port) && options.port >= 0 ? Math.trunc(options.port) : DEFAULT_PORT;
    const host = typeof options.host === 'string' && options.host.length > 0 ? options.host : undefined;
    const effectiveBindHost = host
        || (typeof process.env.OPENCHAMBER_HOST === 'string' && process.env.OPENCHAMBER_HOST.trim().length > 0
            ? process.env.OPENCHAMBER_HOST.trim()
            : '127.0.0.1');
    if (effectiveBindHost !== '127.0.0.1')
        throw new Error('TwinCAT Assistant Chat listens only on 127.0.0.1.');
    managedConfigRuntime = createManagedConfigRuntime({
        fsPromises,
        path,
        dataDir: OPENCHAMBER_DATA_DIR,
        env: process.env,
        agentToolRuntime,
        readSettings: () => readSettingsFromDiskMigrated(),
        isAgentMemoryAvailable: isAgentMemoryFeatureAvailable,
    });
    const uiPassword = typeof options.uiPassword === 'string'
        ? options.uiPassword
        : (typeof process.env.OPENCHAMBER_UI_PASSWORD === 'string' ? process.env.OPENCHAMBER_UI_PASSWORD : null);
    const apiOnly = options.apiOnly === true || isEnvFlagEnabled(process.env.OPENCHAMBER_API_ONLY);
    const attachSignals = options.attachSignals !== false;
    if (typeof options.exitOnShutdown === 'boolean') {
        exitOnShutdown = options.exitOnShutdown;
    }
    console.log(`Starting OpenChamber on port ${port === 0 ? 'auto' : port}`);
    const app = express();
    // TwinCAT Assistant Core delivery boundary. Keep this before every route,
    // including the generic OpenCode proxy and the SPA fallback.
    app.use((req, res, next) => {
        let path;
        try {
            path = decodeURIComponent(req.path).replace(/\/+/g, '/').toLowerCase();
        }
        catch {
            return res.status(400).json({ error: 'Invalid request path' });
        }
        const excluded = /^\/api\/(terminal|browser-control|dev-servers|dictation|tts|git|github|gitlab|source-control|linear|walkthrough|tracked-items|scheduled-tasks|routing|goals|session-assist|session-work|magic-prompts|guests|extensions|openchamber-control|spaces|tunnels|relay|remote-clients|pairing)(\/|$)/.test(path)
            || /^\/api\/fs\/(write|delete|rename|exec|mkdir|clone|reveal)(\/|$)/.test(path)
            || /^\/api\/opencode\/(upgrade|upgrade-status|install-v2)(\/|$)/.test(path)
            || /^\/api\/openchamber\/(update-check|update-install)(\/|$)/.test(path)
            || /^\/api\/notifications\/(push|vapid|apns|subscribe|auto-accept)(\/|$)/.test(path)
            || /^\/auth\/(pairing|device|remote)(\/|$)/.test(path);
        if (excluded)
            return res.status(404).json({ error: 'Feature outside TwinCAT Assistant delivery scope' });
        next();
    });
    const serverStartedAt = new Date().toISOString();
    const packagedClientOrigins = new Set([
        'openchamber-ui://app',
        'capacitor://localhost',
        'http://localhost',
        'https://localhost',
    ]);
    const allowLocalDevOrigins = allowsLocalDevOrigins(process.env);
    // Keeps other sites from framing the app (clickjacking). Routes that serve
    // untrusted documents set a Content-Security-Policy of their own, which
    // replaces this one.
    const frameAncestorsPolicy = buildFrameAncestorsPolicy({
        allowLocalDevOrigins,
        extra: process.env.OPENCHAMBER_FRAME_ANCESTORS,
    });
    // Forwarded headers are believed only from a proxy on this machine or a
    // private network (cloudflared, Docker, a LAN reverse proxy). From anyone
    // else they are the client's own words, and the login rate limit keys on
    // req.ip, so a client must not be able to name its own address.
    app.set('trust proxy', 'loopback, linklocal, uniquelocal');
    // Keep self-hosted instances out of search engines. The app shell is served
    // publicly (it loads before prompting for the UI password), so without this
    // even a password-protected instance gets crawled and indexed. Applies to
    // every response; the robots.txt route makes the intent explicit for crawlers.
    app.use((req, res, next) => {
        res.setHeader('X-Robots-Tag', 'noindex, nofollow');
        // The preview proxy relays a dev server's own pages, whose types and
        // referrer behaviour are that app's business. Everything else is ours:
        // served with real types, and its address (a tunnel host, short-lived
        // auth tokens, session ids) never follows a link out as a Referer.
        if (!req.path.startsWith('/api/preview/proxy/')) {
            res.setHeader('X-Content-Type-Options', 'nosniff');
            res.setHeader('Referrer-Policy', 'same-origin');
            res.setHeader('Content-Security-Policy', frameAncestorsPolicy);
        }
        next();
    });
    app.get('/robots.txt', (_req, res) => {
        res.type('text/plain').send('User-agent: *\nDisallow: /\n');
    });
    app.use((req, res, next) => {
        const origin = typeof req.headers.origin === 'string' ? req.headers.origin : '';
        if (packagedClientOrigins.has(origin) || (allowLocalDevOrigins && isLocalDevClientOrigin(origin))) {
            res.setHeader('Access-Control-Allow-Origin', origin);
            res.setHeader('Access-Control-Allow-Credentials', 'true');
            res.setHeader('Access-Control-Allow-Methods', 'GET,POST,PUT,PATCH,DELETE,OPTIONS');
            // The packaged desktop UI (openchamber-ui://) and the dev UI sit on a
            // different origin, so every custom request header must be listed here or
            // the browser refuses the request at preflight, before it reaches a route.
            res.setHeader('Access-Control-Allow-Headers', 'Content-Type,Authorization,Accept,X-Requested-With,Cache-Control,X-OpenCode-Directory,X-OpenCode-Directory-Encoding,Ngrok-Skip-Browser-Warning,X-OpenChamber-Surface');
            res.setHeader('Access-Control-Expose-Headers', 'x-next-cursor');
            res.setHeader('Vary', 'Origin');
            if (req.method === 'OPTIONS') {
                res.status(204).end();
                return;
            }
        }
        next();
    });
    app.use(compression({
        filter: (req, res) => {
            if (shouldSkipCompression(req, res))
                return false;
            return compression.filter(req, res);
        },
        threshold: 1024,
    }));
    expressApp = app;
    server = http.createServer(app);
    gracefulShutdownRuntime.trackServerConnections(server);
    const bootstrapResult = bootstrapRuntime.setupBaseRoutes(app, {
        process,
        openchamberVersion: OPENCHAMBER_VERSION,
        runtimeName: process.env.OPENCHAMBER_RUNTIME || 'web',
        serverStartedAt,
        gracefulShutdown,
        getHealthSnapshot: () => {
            const launchSpec = resolvedOpencodeBinary && !useWslForOpencode
                ? resolveManagedOpenCodeLaunchSpec(resolvedOpencodeBinary)
                : null;
            return {
                openCodePort,
                openCodeRunning: Boolean(openCodePort && isOpenCodeReady && !isRestartingOpenCode),
                openCodeSecureConnection: isOpenCodeConnectionSecure(),
                openCodeAuthSource: openCodeAuthSource || null,
                openCodeApiPrefix: '',
                openCodeApiPrefixDetected: true,
                isOpenCodeReady,
                lastOpenCodeError,
                lastOpenCodeLaunchDiagnostics,
                lastOpenCodeHealthFailure,
                lastManagedOpenCodeProcess,
                lastOpenCodeRestartDiagnostics,
                opencodeBinaryResolved: resolvedOpencodeBinary || null,
                opencodeBinarySource: resolvedOpencodeBinarySource || null,
                opencodeLaunchBinary: launchSpec?.binary || null,
                opencodeLaunchArgs: launchSpec?.args || [],
                opencodeLaunchWrapperType: launchSpec?.wrapperType || null,
                opencodeViaWsl: useWslForOpencode,
                opencodeWslBinary: resolvedWslBinary || null,
                opencodeWslPath: resolvedWslOpencodePath || null,
                opencodeWslDistro: resolvedWslDistro || null,
                nodeBinaryResolved: resolvedNodeBinary || null,
                bunBinaryResolved: resolvedBunBinary || null,
                desktopNotifyEnabled: ENV_DESKTOP_NOTIFY,
                planModeExperimentalEnabled: PLAN_MODE_EXPERIMENT_ENABLED,
                apiOnly,
            };
        },
        getServerPort: () => getActivePort(),
        getTunnelUrl: () => null,
        verboseRequestLogs: OPENCHAMBER_VERBOSE_REQUEST_LOGS,
        uiPassword,
        getServerId: () => localServerId,
        readSettingsFromDiskMigrated,
        ensureGlobalWatcherStarted,
        writeSettingsToDisk,
        getUiNotificationClients: () => uiNotificationClients,
        writeSseEvent,
        sessionRuntime,
        fs,
        os,
        path,
        server,
        __dirname,
        openchamberDataDir: OPENCHAMBER_DATA_DIR,
        modelsDevApiUrl: MODELS_DEV_API_URL,
        modelsMetadataCacheTtl: MODELS_METADATA_CACHE_TTL,
        fetchFreeZenModels,
        getCachedZenModels,
        agentToolRuntime,
    });
    uiAuthController = bootstrapResult.uiAuthController;
    // Browser-control HTTP entry points are not registered.
    // One scanner backs both discovery and the tunnel allowlist, so a port the
    // user can see is exactly a port the tunnel will dial.
    await featureRoutesRuntime.registerRoutes(app, {
        messageSearchRuntime,
        crypto,
        fs,
        os,
        path,
        fsPromises,
        spawn,
        resolveGitBinaryForSpawn,
        createFsSearchRuntime: createFsSearchRuntimeFactory,
        openchamberDataDir: OPENCHAMBER_DATA_DIR,
        openchamberVersion: OPENCHAMBER_VERSION,
        builtInExtensionsDir: options.builtInExtensionsDir,
        openchamberUserConfigRoot: OPENCHAMBER_USER_CONFIG_ROOT,
        managedChatsRoot: OPENCHAMBER_CHATS_DIR,
        normalizeDirectoryPath,
        resolveProjectDirectory,
        resolveOptionalProjectDirectory,
        validateDirectoryPath,
        readCustomThemesFromDisk,
        saveImportedTheme,
        deleteImportedTheme,
        refreshOpenCodeAfterConfigChange,
        getOpenCodeResolutionSnapshot,
        getOpenCodeUpgradeCapability,
        getOpenCodeCompatibility,
        installOpenCodeV2: async () => {
            const binary = await installOpenCodeV2({
                env: { ...getLoginShellEnvSnapshot(), ...process.env, PATH: buildManagedOpenCodePath() },
            });
            await persistSettings({ opencodeBinary: binary });
            await refreshOpenCodeAfterConfigChange('OpenCode v2 installation');
            await waitForOpenCodeReady();
        },
        upgradeOpenCodeCli: () => runOpenCodeCliUpgrade(resolveManagedOpenCodeLaunchSpec(lastOpenCodeLaunchDiagnostics?.sourceBinary || resolvedOpencodeBinary), { env: { ...getLoginShellEnvSnapshot(), ...process.env, PATH: buildManagedOpenCodePath() }, cwd: os.homedir() }),
        formatSettingsResponse,
        readSettingsFromDisk,
        readSettingsFromDiskMigrated,
        persistSettings,
        sanitizeProjects,
        sanitizeSkillCatalogs,
        isUnsafeSkillRelativePath,
        buildOpenCodeUrl,
        getOpenCodeAuthHeaders,
        getOpenCodePort: () => openCodePort,
        // Dev-server discovery must not offer OpenChamber's own listeners back to
        // the user as something to preview.
        getOwnPorts: () => [port, openCodePort].filter((value) => Number.isInteger(value) && value > 0),
        getActivePort: () => {
            const address = server?.address?.();
            return address && Number.isInteger(address.port) ? address.port : null;
        },
        // A pipe listener reports a string here, which has no address to bind back to.
        getActiveHost: () => server?.address?.()?.address ?? null,
        buildAugmentedPath,
        projectConfigRuntime,
        projectContextRuntime,
        agentMemoryRuntime,
        isAgentMemoryEnabled,
        sessionKnowledgeRuntime,
        openChamberSessionService,
        waitForOpenCodeReady,
        emitSessionCreatedEvent,
        getOpenChamberEventClients: () => uiOpenChamberEventClients,
        writeSseEvent,
        globalEventHub: globalMessageStreamHub,
        worktreeBootstrapStore,
        messageQueueRuntime,
    });
    // After bootstrap: the upgrade gate needs the real UI auth controller.
    const startupPipelineResult = await startupPipelineRuntime.run({
        app,
        server,
        express,
        fs,
        path,
        uiAuthController,
        buildAugmentedPath,
        searchPathFor,
        isExecutable,
        isRequestOriginAllowed,
        rejectWebSocketUpgrade,
        buildOpenCodeUrl,
        getOpenCodeAuthHeaders,
        globalEventHub: globalMessageStreamHub,
        processForwardedEventPayload,
        messageStreamWsClients: uiNotificationWsClients,
        upstreamStallTimeoutMs: getUpstreamStallTimeoutMs,
        setupProxy,
        scheduleOpenCodeApiDetection,
        bootstrapOpenCodeAtStartup,
        // Git's credential helper reaches the server through a file that names
        // the port, so it is written once the port is known and before OpenCode,
        // whose shells will use it, starts.
        // No repository credential endpoint is published by Assistant Chat.
        triggerHealthCheck,
        staticRoutesRuntime,
        process,
        crypto,
        readSettingsFromDiskMigrated,
        gracefulShutdown,
        getSignalsAttached: () => signalsAttached,
        setSignalsAttached: (value) => {
            signalsAttached = value;
        },
        syncToHmrState,
        host,
        port,
        attachSignals,
        apiOnly,
    });
    terminalRuntime = startupPipelineResult.terminalRuntime;
    dictationRuntime = startupPipelineResult.dictationRuntime;
    messageStreamRuntime = startupPipelineResult.messageStreamRuntime;
    return {
        expressApp: app,
        httpServer: server,
        getPort: () => getActivePort(),
        getOpenCodePort: () => openCodePort,
        getTunnelUrl: () => null,
        isReady: () => isOpenCodeReady,
        getManagedOpenCodePreflight: () => openCodeLifecycleRuntime.getManagedOpenCodePreflight(),
        restartOpenCode: () => restartOpenCode(),
        getOpenCodeProcessInfo: () => {
            const managed = Boolean((openCodeProcess || openCodePort) && !ENV_SKIP_OPENCODE_START && !isExternalOpenCode);
            // Only ever expose pid/port for a server WE manage. The Electron-side
            // killer kills by port (lsof + kill -KILL), so returning a port we don't
            // own — e.g. an external/desktop OpenCode on 4096 we attached to — would
            // let a single miscomputed `managed` flag take down the user's separate
            // server. Structurally withhold what isn't ours so the killer has no
            // target, instead of relying on the flag check alone.
            return {
                managed,
                pid: managed && typeof openCodeProcess?.pid === 'number' ? openCodeProcess.pid : null,
                port: managed ? openCodePort : null,
            };
        },
        stop: (shutdownOptions = {}) => gracefulShutdown({ exitProcess: shutdownOptions.exitProcess ?? false }),
    };
}

export { main as startWebUiServer, gracefulShutdown };
