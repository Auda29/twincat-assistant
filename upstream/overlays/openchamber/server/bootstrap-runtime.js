export const createBootstrapRuntime = (dependencies) => {
    const { createUiAuth, registerServerStatusRoutes, registerCommonRequestMiddleware, registerAuthAndAccessRoutes, registerNotificationRoutes, registerOpenChamberRoutes, express, } = dependencies;
    const setupBaseRoutes = (app, options) => {
        const { process, openchamberVersion, runtimeName, serverStartedAt, gracefulShutdown, getHealthSnapshot, getServerPort, verboseRequestLogs, uiPassword, getServerId, readSettingsFromDiskMigrated, ensureGlobalWatcherStarted, writeSettingsToDisk, getUiNotificationClients, writeSseEvent, sessionRuntime, fs, os, path, server, __dirname, openchamberDataDir, modelsDevApiUrl, modelsMetadataCacheTtl, fetchFreeZenModels, getCachedZenModels, } = options;
        const uiAuthController = createUiAuth({
            password: uiPassword,
            readSettingsFromDiskMigrated,
        });
        if (uiAuthController.enabled) {
            console.log('UI password protection enabled for browser sessions');
        }
        registerServerStatusRoutes(app, {
            express,
            process,
            openchamberVersion,
            runtimeName,
            serverStartedAt,
            gracefulShutdown,
            getHealthSnapshot,
            getServerId,
            getServerPort,
            uiAuthController,
        });
        registerCommonRequestMiddleware(app, { express, verboseRequestLogs });
        registerAuthAndAccessRoutes(app, {
            express,
            uiAuthController,
            getServerId,
            readSettingsFromDiskMigrated,
        });
        registerNotificationRoutes(app, {
            uiAuthController,
            ensureGlobalWatcherStarted,
            readSettingsFromDiskMigrated,
            writeSettingsToDisk,
            getUiNotificationClients,
            writeSseEvent,
            getSessionActivitySnapshot: sessionRuntime.getSessionActivitySnapshot,
            getSessionStateSnapshot: sessionRuntime.getSessionStateSnapshot,
            getPendingBlockingRequestsSnapshot: sessionRuntime.getPendingBlockingRequestsSnapshot,
            getSessionAttentionSnapshot: sessionRuntime.getSessionAttentionSnapshot,
            getSessionState: sessionRuntime.getSessionState,
            getSessionAttentionState: sessionRuntime.getSessionAttentionState,
            markSessionViewed: sessionRuntime.markSessionViewed,
            markSessionUnviewed: sessionRuntime.markSessionUnviewed,
            markUserMessageSent: sessionRuntime.markUserMessageSent,
        });
        registerOpenChamberRoutes(app, {
            fs,
            os,
            path,
            process,
            server,
            __dirname,
            openchamberDataDir,
            modelsDevApiUrl,
            modelsMetadataCacheTtl,
            readSettingsFromDiskMigrated,
            fetchFreeZenModels,
            getCachedZenModels,
        });
        return {
            uiAuthController,
        };
    };
    return {
        setupBaseRoutes,
    };
};
