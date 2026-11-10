import { recordStartupPerformance } from './startup-performance.js';
export const createStartupPipelineRuntime = (dependencies) => {
    const { createMessageStreamWsRuntime, createServerStartupRuntime, } = dependencies;
    const run = async (options) => {
        const pipelineStartedAt = performance.now();
        recordStartupPerformance('web.pipeline.start');
        const { app, server, express, fs, path, uiAuthController, buildAugmentedPath, searchPathFor, isExecutable, isRequestOriginAllowed, rejectWebSocketUpgrade, buildOpenCodeUrl, getOpenCodeAuthHeaders, globalEventHub, processForwardedEventPayload, messageStreamWsClients, triggerHealthCheck, upstreamStallTimeoutMs, setupProxy, scheduleOpenCodeApiDetection, bootstrapOpenCodeAtStartup, onListenerReady = null, staticRoutesRuntime, process, crypto, normalizeTunnelBootstrapTtlMs, readSettingsFromDiskMigrated, tunnelAuthController, startTunnelWithNormalizedRequest, gracefulShutdown, getSignalsAttached, setSignalsAttached, syncToHmrState, TUNNEL_MODE_QUICK, TUNNEL_MODE_MANAGED_LOCAL, TUNNEL_MODE_MANAGED_REMOTE, host, port, startupTunnelRequest, onTunnelReady, tunnelRuntimeContext, attachSignals, apiOnly, } = options;
        const messageStreamRuntime = createMessageStreamWsRuntime({
            server,
            uiAuthController,
            isRequestOriginAllowed,
            rejectWebSocketUpgrade,
            buildOpenCodeUrl,
            getOpenCodeAuthHeaders,
            globalEventHub,
            processForwardedEventPayload,
            wsClients: messageStreamWsClients,
            triggerHealthCheck,
            upstreamStallTimeoutMs,
        });
        setupProxy(app);
        if (apiOnly) {
            staticRoutesRuntime.registerApiOnlyFallbackRoutes(app);
        }
        else {
            staticRoutesRuntime.registerStaticRoutes(app);
        }
        const serverStartupRuntime = createServerStartupRuntime({
            process,
            crypto,
            server,
            normalizeTunnelBootstrapTtlMs,
            readSettingsFromDiskMigrated,
            tunnelAuthController,
            startTunnelWithNormalizedRequest,
            gracefulShutdown,
            getSignalsAttached,
            setSignalsAttached,
            syncToHmrState,
            TUNNEL_MODE_QUICK,
            TUNNEL_MODE_MANAGED_LOCAL,
            TUNNEL_MODE_MANAGED_REMOTE,
        });
        const bindHost = serverStartupRuntime.resolveBindHost(host);
        const startupResult = await serverStartupRuntime.startListeningAndMaybeTunnel({
            port,
            bindHost,
            startupTunnelRequest,
            onTunnelReady,
        });
        recordStartupPerformance('web.listener.ready', {
            durationMs: performance.now() - pipelineStartedAt,
        });
        if (onListenerReady)
            await onListenerReady();
        scheduleOpenCodeApiDetection();
        void bootstrapOpenCodeAtStartup();
        serverStartupRuntime.attachProcessHandlers({ attachSignals });
        return {
            messageStreamRuntime,
        };
    };
    return {
        run,
    };
};
