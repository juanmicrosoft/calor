import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile, rm } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { randomUUID } from 'node:crypto';

const doc = await readFile('content/getting-started/hello-world.mdx', 'utf8');
const cli = path.resolve('../src/Calor.Compiler/bin/Debug/net10.0/calor.dll');
const workspace = path.resolve('test-results', `onboarding-${randomUUID()}`);
await mkdir(path.join(workspace, 'scratch'), { recursive: true });
// Keep this in the worktree without inheriting repository-only MSBuild policy.
await writeFile(path.join(workspace, 'Directory.Build.props'), '<Project />');
await writeFile(path.join(workspace, 'Directory.Build.targets'), '<Project />');
await writeFile(path.join(workspace, 'Directory.Packages.props'), '<Project />');
const env = { ...process.env, TMPDIR: path.join(workspace, 'scratch'),
  TMP: path.join(workspace, 'scratch'), TEMP: path.join(workspace, 'scratch'),
  CALOR_TELEMETRY: '0', DOTNET_NOLOGO: '1' };
const section = title => {
  const body = doc.split(`## ${title}\n`)[1]?.split('\n## ')[0];
  assert.ok(body, `Missing published section: ${title}`);
  return body;
};
const blocks = text => [...text.matchAll(/```bash\n([\s\S]*?)```/g)].map(match => match[1].trim());
let cwd = workspace;
function run(command) {
  const [program, ...args] = command.split(' ');
  const result = program === 'calor'
    ? spawnSync('dotnet', [cli, ...args], { cwd, env, encoding: 'utf8' })
    : spawnSync(program, args, { cwd, env, encoding: 'utf8' });
  assert.equal(result.status, 0, `${command}\n${result.error || ''}\n${result.stdout}\n${result.stderr}`);
  return result.stdout.replace(/\r/g, '').trim();
}
try {
  for (const command of blocks(section('Create a new directory'))[0].split('\n')) {
    if (command.startsWith('cd ')) cwd = path.resolve(cwd, command.slice(3));
    else run(command);
  }
  const source = section('Save your source').match(/```calor\n([\s\S]*?)```/)?.[1];
  assert.ok(source, 'Missing published Calor source');
  await writeFile(path.join(cwd, 'Program.calr'), source);
  const expected = section('Run without a project').match(/```text\n([\s\S]*?)```/)?.[1].trim();
  assert.equal(expected, 'Hello from Calor!');
  assert.equal(run(blocks(section('Run without a project'))[0]), expected);

  const [create, remove, compileAndRun] = blocks(section('Optional: inspect a .NET project'));
  const [compile, execute] = compileAndRun.split('\n');
  run(create);
  assert.match(await readFile(path.join(cwd, 'Program.cs'), 'utf8'), /Hello, World!/);
  run(compile);
  // Demonstrate the original entry-point conflict before applying the documented fix.
  assert.match(run(execute), /Hello, World!/);
  run(remove);
  run(compile);
  assert.equal(run(execute), expected);
  console.log('Published standalone and .NET project recipes passed; template conflict reproduced and fixed.');

  const workflow = await readFile('content/getting-started/how-it-works.mdx', 'utf8');
  const workflowSource = workflow.match(/```calor\n([\s\S]*?)```/)?.[1];
  assert.ok(workflowSource, 'Missing complete How It Works example');
  const workflowCommands = blocks(workflow)[1].split('\n');
  await writeFile(path.join(cwd, 'Program.calr'), workflowSource);
  run(workflowCommands[0]);
  assert.equal(run(workflowCommands[1]), expected);
  await writeFile(path.join(cwd, 'Program.calr'), workflowSource.replace('Hello from Calor!', 'Hello again!'));
  run(workflowCommands[0]);
  assert.equal(run(workflowCommands[1]), 'Hello again!');
  console.log('Published How It Works compile/run/revise workflow passed.');

  const effects = await readFile('content/benchmarking/metrics/effect-discipline.mdx', 'utf8');
  const priceSource = effects.match(/```calor\n([\s\S]*?)```/)?.[1];
  assert.ok(priceSource, 'Missing complete Effect Discipline example');
  await writeFile(path.join(cwd, 'Price.calr'), priceSource);
  assert.equal(run(blocks(effects)[0]), '60');
  console.log('Published Effect Discipline explicit-input program passed.');
} finally {
  await rm(workspace, { recursive: true, force: true });
}

await import('./overflow-guidance.mjs');
