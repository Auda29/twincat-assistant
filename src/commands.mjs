import { lstat, mkdir, open, readFile, rename, unlink } from 'node:fs/promises';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';

// TwinCAT slash commands for the read-only agent. OpenCode reads them as
// Markdown files from `commands/` next to the protected opencode.json, so the
// verified policy file and existing project workspaces stay unchanged.
// Names are English; answers follow the language of the conversation.
const LANGUAGE = 'Reply in the language of this conversation; if there is no earlier message, reply in German.';
const ATTACHED = 'Use the TwinCAT POU attached to this message (the "Aktiver Baustein" chip or another attached POU). If no POU is attached, ask me to attach one with Anhängen.';

export const TWINCAT_COMMANDS = Object.freeze({
  explain: {
    description: 'Explain the attached POU: purpose, interface, sequence and states',
    template: `${ATTACHED}
Explain it for a PLC developer: its purpose, the inputs, outputs and VAR_IN_OUT, the control flow or step sequence, and important states, timers and error handling. Look up called POUs, methods and data types with the engineering tools when that helps the explanation. Point out where the attached snapshot may differ from the saved project files.
${LANGUAGE}
$ARGUMENTS`,
  },
  review: {
    description: 'Review the attached POU for likely bugs without changing anything',
    template: `${ATTACHED}
Review it and report concrete findings only: possible bugs, missing initialization or reset, overflow or division by zero, variables written in several places, unreachable or stuck steps, timer misuse and missing error handling. For each finding name the element or line, why it is a problem and a suggested fix as text. Mark uncertain findings as such. Do not change any file.
${LANGUAGE}
$ARGUMENTS`,
  },
  variable: {
    description: 'Show where a variable is declared, read and written in the project',
    template: `Find every place where the variable \`$ARGUMENTS\` is declared, read and written in the saved PLC project, using the engineering search and read tools. List declarations, write accesses and read accesses separately, each with POU and line. Then explain under which conditions the variable is set or changed. If no variable name was given, ask for one.
${LANGUAGE}`,
  },
  docs: {
    description: 'Search the Beckhoff documentation and answer with citations',
    template: `Research this topic in the Beckhoff documentation with the knowledge tools: $ARGUMENTS
Call knowledge_tc_get_corpus_status first. Answer only from the returned articles, cite every source with its title and ID, and say clearly when nothing relevant was found. If no topic was given, ask for one.
${LANGUAGE}`,
  },
  'pou-docs': {
    description: 'Look up the library blocks used in the attached POU in the Beckhoff documentation',
    template: `${ATTACHED}
List the library function blocks, functions and types it uses, for example from Tc2_MC2 or Tc2_Standard, and skip project-own POUs. Look each one up in the Beckhoff documentation with the knowledge tools and explain its parameters as they are used here, with citations.
${LANGUAGE}
$ARGUMENTS`,
  },
});

export function commandFile({ description, template }) {
  return `---\ndescription: ${JSON.stringify(description)}\nagent: twincat-readonly\n---\n${template.trim()}\n`;
}

// Writes the managed command files. Only these names are touched; a redirected
// directory or a non-file entry is refused instead of followed.
export async function installTwinCatCommands(configDirectory) {
  const directory = join(configDirectory, 'commands');
  try { await mkdir(directory, { mode: 0o700 }); }
  catch (error) { if (error.code !== 'EEXIST') throw error; }
  const metadata = await lstat(directory);
  if (!metadata.isDirectory() || metadata.isSymbolicLink()) throw new Error('Command directory must be a regular directory');
  for (const [name, command] of Object.entries(TWINCAT_COMMANDS)) {
    const path = join(directory, name + '.md');
    const content = commandFile(command);
    const existing = await lstat(path).catch(error => { if (error.code === 'ENOENT') return null; throw error; });
    if (existing && (!existing.isFile() || existing.isSymbolicLink())) throw new Error('Command file must be a regular file');
    if (existing && await readFile(path, 'utf8') === content) continue;
    const temporary = join(directory, '.' + name + '-' + randomUUID() + '.tmp');
    try {
      const handle = await open(temporary, 'wx', 0o600);
      try { await handle.writeFile(content); await handle.sync(); } finally { await handle.close(); }
      await rename(temporary, path);
    } finally { await unlink(temporary).catch(error => { if (error.code !== 'ENOENT') throw error; }); }
  }
  return Object.keys(TWINCAT_COMMANDS);
}
