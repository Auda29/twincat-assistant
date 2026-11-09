import { registerFsRoutes } from '../fs/routes.js';
import { registerSessionFoldersRoutes } from '../session-folders/routes.js';
import { registerProjectDirectoryRoutes } from '../projects/routes.js';
import { registerMessageQueueRoutes } from '../message-queue/runtime.js';
import { registerMessageSearchRoutes } from '../message-search/routes.js';
import { registerOpenChamberSessionRoutes } from '../openchamber-sessions/routes.js';
import { registerMarkdownImageGrantRoutes } from '../markdown-image-grants/routes.js';
import { registerConfigEntityRoutes } from './config-entity-routes.js';
import { registerSettingsUtilityRoutes } from './core-routes.js';
import { registerOpenCodeRoutes } from './routes.js';
import { getProviderSources, removeProviderConfig, upsertProviderConfig } from './providers.js';
import { getAgentSources, getAgentConfig, getAgentPermissions, createAgent, updateAgent, deleteAgent } from './agents.js';
import { getCommandSources, getCommandConfig, createCommand, updateCommand, deleteCommand } from './commands.js';
import { listMcpConfigs, getMcpConfig, createMcpConfig, updateMcpConfig, deleteMcpConfig } from './mcp.js';
import { listSnippets, getSnippet, createSnippet, updateSnippet, deleteSnippet, expandSnippets } from './snippets.js';

// Product composition: provider/model settings, conversations, attachments and
// local project context. Upstream Git accounts, worktrees, guests, scheduling,
// routing, walkthroughs and remote control are not registered or imported here.
export const createFeatureRoutesRuntime = ({ clientReloadDelayMs }) => ({
  async registerRoutes(app, dependencies) {
    // Chat metadata broadcasts share this connection with the historical
    // scheduler. Keep the transport independently of every scheduling service.
    app.get('/api/openchamber/events', (_req, res) => {
      res.setHeader('Content-Type', 'text/event-stream; charset=utf-8');
      res.setHeader('Cache-Control', 'no-cache, no-transform');
      res.setHeader('Connection', 'keep-alive');
      res.setHeader('X-Accel-Buffering', 'no');
      res.flushHeaders?.();
      res.openchamberBrowserCapable = false;
      res.openchamberConnectionId = dependencies.crypto.randomUUID();
      const clients = dependencies.getOpenChamberEventClients();
      clients.add(res);
      let heartbeat;
      const cleanup = () => { clearInterval(heartbeat); clients.delete(res); };
      _req.once('close', cleanup);
      res.once('error', cleanup);
      dependencies.writeSseEvent(res, {
        type: 'openchamber:event-stream-ready',
        properties: { connectedAt: Date.now(), connectionId: res.openchamberConnectionId },
      });
      heartbeat = setInterval(() => {
        if (res.destroyed || res.writableEnded) return cleanup();
        try { dependencies.writeSseEvent(res, { type: 'openchamber:heartbeat', properties: { timestamp: Date.now() } }); }
        catch { cleanup(); }
      }, 25000);
    });
    app.get('/api/twincat/product', (_req, res) => res.json({
      name: 'TwinCAT Assistant',
      version: process.env.TWINCAT_ASSISTANT_VERSION ?? null,
    }));
    const settings = { ...dependencies, clientReloadDelayMs };
    registerSettingsUtilityRoutes(app, settings);
    registerOpenCodeRoutes(app, { ...settings, getProviderSources, removeProviderConfig, upsertProviderConfig });
    registerConfigEntityRoutes(app, {
      ...settings,
      getAgentSources, getAgentConfig, getAgentPermissions, createAgent, updateAgent, deleteAgent,
      getCommandSources, getCommandConfig, createCommand, updateCommand, deleteCommand,
      listMcpConfigs, getMcpConfig, createMcpConfig, updateMcpConfig, deleteMcpConfig,
      listSnippets, getSnippet, createSnippet, updateSnippet, deleteSnippet, expandSnippets,
    });
    registerMessageQueueRoutes(app, dependencies.messageQueueRuntime);
    registerMessageSearchRoutes(app, { messageSearchRuntime: dependencies.messageSearchRuntime });
    registerOpenChamberSessionRoutes(app, { ...settings, sessionService: dependencies.openChamberSessionService });
    registerMarkdownImageGrantRoutes(app, settings);
    registerProjectDirectoryRoutes(app, settings);
    registerSessionFoldersRoutes(app, settings);
    registerFsRoutes(app, settings);
  },
});
