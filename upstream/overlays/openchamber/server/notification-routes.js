export const NOTIFICATION_SSE_HEARTBEAT_INTERVAL_MS = 20000;
export const registerNotificationRoutes = (app, dependencies) => {
    const { uiAuthController, ensureGlobalWatcherStarted, readSettingsFromDiskMigrated, writeSettingsToDisk, getUiNotificationClients, writeSseEvent, getSessionActivitySnapshot, getSessionStateSnapshot, getPendingBlockingRequestsSnapshot, getSessionAttentionSnapshot, getSessionState, getSessionAttentionState, markSessionViewed, markSessionUnviewed, markUserMessageSent, setAutoAcceptSession, } = dependencies;
    const ensureSessionWatcher = async () => {
        if (typeof ensureGlobalWatcherStarted !== 'function') {
            return;
        }
        try {
            await ensureGlobalWatcherStarted();
        }
        catch (error) {
            console.warn('[OpenCodeWatcher] lazy start failed:', error?.message ?? error);
        }
    };
    app.get('/api/notifications/stream', async (req, res) => {
        await ensureSessionWatcher();
        const uiToken = uiAuthController?.ensureSessionToken
            ? await uiAuthController.ensureSessionToken(req, res)
            : getUiSessionTokenFromRequest(req);
        if (!uiToken) {
            return;
        }
        res.setHeader('Content-Type', 'text/event-stream; charset=utf-8');
        res.setHeader('Cache-Control', 'no-cache, no-transform');
        res.setHeader('Connection', 'keep-alive');
        res.setHeader('X-Accel-Buffering', 'no');
        res.flushHeaders?.();
        const clients = getUiNotificationClients();
        clients.add(res);
        let closed = false;
        let heartbeatTimer = null;
        const cleanup = () => {
            if (closed) {
                return;
            }
            closed = true;
            if (heartbeatTimer) {
                clearInterval(heartbeatTimer);
                heartbeatTimer = null;
            }
            clients.delete(res);
        };
        req.on('close', cleanup);
        res.on('error', cleanup);
        const flushSse = () => {
            res.flush?.();
        };
        heartbeatTimer = setInterval(() => {
            if (closed || res.writableEnded || res.destroyed) {
                cleanup();
                return;
            }
            try {
                res.write(':heartbeat\n\n');
                flushSse();
            }
            catch {
                cleanup();
            }
        }, NOTIFICATION_SSE_HEARTBEAT_INTERVAL_MS);
        try {
            writeSseEvent(res, {
                type: 'openchamber:notification-stream-ready',
                properties: { uiToken },
            });
            flushSse();
        }
        catch {
            cleanup();
        }
    });
    app.get('/api/session-activity', (_req, res) => {
        void ensureSessionWatcher();
        res.json(getSessionActivitySnapshot());
    });
    app.get('/api/sessions/snapshot', async (_req, res) => {
        await ensureSessionWatcher();
        res.json({
            statusSessions: getSessionStateSnapshot(),
            attentionSessions: getSessionAttentionSnapshot(),
            serverTime: Date.now(),
        });
    });
    // Cross-project seed for clients that do not initialize every directory:
    // live status per session plus the permission requests and forms still
    // waiting for an answer. Both come from the server's single upstream stream.
    app.get('/api/sessions/status', async (_req, res) => {
        await ensureSessionWatcher();
        const snapshot = getSessionStateSnapshot();
        res.json({
            sessions: snapshot,
            pending: typeof getPendingBlockingRequestsSnapshot === 'function' ? getPendingBlockingRequestsSnapshot() : {},
            serverTime: Date.now(),
        });
    });
    app.get('/api/sessions/:id/status', async (req, res) => {
        await ensureSessionWatcher();
        const sessionId = req.params.id;
        const state = getSessionState(sessionId);
        if (!state) {
            return res.status(404).json({
                error: 'Session not found or no state available',
                sessionId,
            });
        }
        return res.json({
            sessionId,
            ...state,
        });
    });
    app.get('/api/sessions/attention', async (_req, res) => {
        await ensureSessionWatcher();
        const snapshot = getSessionAttentionSnapshot();
        res.json({
            sessions: snapshot,
            serverTime: Date.now(),
        });
    });
    app.get('/api/sessions/:id/attention', async (req, res) => {
        await ensureSessionWatcher();
        const sessionId = req.params.id;
        const state = getSessionAttentionState(sessionId);
        if (!state) {
            return res.status(404).json({
                error: 'Session not found or no attention state available',
                sessionId,
            });
        }
        return res.json({
            sessionId,
            ...state,
        });
    });
    app.post('/api/sessions/:id/view', (req, res) => {
        const sessionId = req.params.id;
        const clientId = req.headers['x-client-id'] || req.ip || 'anonymous';
        markSessionViewed(sessionId, clientId);
        return res.json({
            success: true,
            sessionId,
            viewed: true,
        });
    });
    app.post('/api/sessions/:id/unview', (req, res) => {
        const sessionId = req.params.id;
        const clientId = req.headers['x-client-id'] || req.ip || 'anonymous';
        markSessionUnviewed(sessionId, clientId);
        return res.json({
            success: true,
            sessionId,
            viewed: false,
        });
    });
    app.post('/api/sessions/:id/message-sent', (req, res) => {
        const sessionId = req.params.id;
        markUserMessageSent(sessionId);
        return res.json({
            success: true,
            sessionId,
            messageSent: true,
        });
    });
};
