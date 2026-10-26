import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const source = path => readFile(new URL(`../native/IdeHost/${path}`, import.meta.url), 'utf8');

// Source contracts, not proof of shell installation or live menu/tool-window loading.
test('native assistant button uses the existing Other Windows group directly', async () => {
  const vsct = (await source('Commands.vsct')).replace(/<!--[\s\S]*?-->/g, '');
  const button = vsct.match(/<Button\b[^>]*\bid="OpenAssistant"[^>]*>([\s\S]*?)<\/Button>/)?.[1];
  assert.ok(button, 'the tool-window command must be defined');
  assert.match(button, /<Parent\s+guid="guidSHLMainMenu"\s+id="IDG_VS_WNDO_OTRWNDWS1"\s*\/>/);
  assert.equal((button.match(/<Parent\b/g) ?? []).length, 1);
  assert.doesNotMatch(vsct, /<Group\b|\bAssistantGroup\b/, 'do not nest a group inside the shell group');
  assert.match(button, /<ButtonText>TwinCAT Assistant<\/ButtonText>/);

  const packageSource = await source('AssistantPackage.cs');
  const commandGuid = vsct.match(/<GuidSymbol\s+name="guidCommands"\s+value="\{([^}]+)\}"/)?.[1];
  const commandId = vsct.match(/<IDSymbol\s+name="OpenAssistant"\s+value="(0x[\da-f]+)"/i)?.[1];
  assert.ok(commandGuid);
  assert.ok(commandId);
  assert.ok(packageSource.includes(`new CommandID(new Guid("${commandGuid}"), ${commandId})`));
});

test('corrected native menu retains its resource name and advances the merge revision', async () => {
  const packageSource = await source('AssistantPackage.cs');
  const revision = Number(packageSource.match(/\[ProvideMenuResource\("Menus\.ctmenu",\s*(\d+)\)\]/)?.[1]);
  assert.ok(revision >= 2, 'an installed version-1 menu must be eligible for remerge');
  assert.match(await source('Host.props'), /<ResourceName>Menus\.ctmenu<\/ResourceName>/);
  for (const architecture of ['X86', 'Amd64']) {
    assert.match(await source(`${architecture}/source.extension.vsixmanifest`), /<Identity\b[^>]*\bVersion="0\.1\.3"/);
  }
});
