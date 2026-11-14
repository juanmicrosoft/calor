import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile, rm } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { randomUUID } from 'node:crypto';

const doc = await readFile('content/getting-started/hello-world.mdx', 'utf8');
const cli = path.resolve('../src/Calor.Compiler/bin/Debug/net10.0/calor.dll');
const runtime = path.resolve('../src/Calor.Runtime/bin/Debug/net10.0/Calor.Runtime.dll');
const workspace = path.resolve('test-results', `onboarding-${randomUUID()}`);
await mkdir(path.join(workspace, 'scratch'), { recursive: true });
await mkdir(path.join(workspace, 'home'));
// Keep this in the worktree without inheriting repository-only MSBuild policy.
await writeFile(path.join(workspace, 'Directory.Build.props'), '<Project />');
await writeFile(path.join(workspace, 'Directory.Build.targets'), '<Project />');
await writeFile(path.join(workspace, 'Directory.Packages.props'), '<Project />');
const env = { ...process.env, TMPDIR: path.join(workspace, 'scratch'),
  TMP: path.join(workspace, 'scratch'), TEMP: path.join(workspace, 'scratch'),
  HOME: path.join(workspace, 'home'), DOTNET_CLI_HOME: path.join(workspace, 'home'),
  XDG_CONFIG_HOME: path.join(workspace, 'home', '.config'),
  CALOR_TELEMETRY: '0', DOTNET_NOLOGO: '1' };
const section = (title, document = doc) => {
  const body = document.split(`## ${title}\n`)[1]?.split('\n## ')[0];
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
  const homepage = await readFile('src/components/landing/QuickStart.tsx', 'utf8');
  const gettingStarted = (await readFile('content/getting-started/index.mdx', 'utf8')).replace(/\r/g, '');
  const setup = section('Add Calor to a .NET project', gettingStarted);
  const createProject = blocks(setup)[0].split('\n').filter(line => line && !line.startsWith('#'));
  const initCommands = blocks(setup).filter(command => command.startsWith('calor init '));
  assert.deepEqual(initCommands, ['claude', 'codex', 'gemini', 'github'].map(agent => `calor init --ai ${agent}`));
  assert.ok(homepage.includes('<code>dotnet tool install -g calor</code>'));
  assert.ok(!homepage.includes('Program.calr'), 'Homepage must not ask the reader to write a source file');
  const instructions = {
    claude: 'CLAUDE.md', codex: 'AGENTS.md', gemini: 'GEMINI.md',
    github: '.github/copilot-instructions.md',
  };
  for (const command of initCommands) {
    assert.ok(homepage.includes(command), `Missing homepage setup: ${command}`);
    const agent = command.split(' ').at(-1);
    for (const existing of [false, true]) {
      cwd = path.join(workspace, `${agent}-${existing ? 'existing' : 'new'}`);
      await mkdir(cwd);
      for (const step of createProject) {
        if (step.startsWith('cd ')) cwd = path.resolve(cwd, step.slice(3));
        else run(step);
      }
      // Keep repository-root agent configuration inside this isolated fixture.
      run('git init --quiet');
      if (existing) {
        await writeFile(path.join(cwd, 'Program.cs'), 'Console.WriteLine("Existing application");\n');
        await writeFile(path.join(cwd, 'ExistingFeature.cs'), 'public class ExistingFeature { public int Value => 42; }\n');
      }
      const originalSource = await readFile(path.join(cwd, 'Program.cs'), 'utf8');
      const originalProject = await readFile(path.join(cwd, 'MyApp.csproj'), 'utf8');
      run(command);
      assert.equal(await readFile(path.join(cwd, 'Program.cs'), 'utf8'), originalSource);
      if (existing) {
        assert.equal(await readFile(path.join(cwd, 'ExistingFeature.cs'), 'utf8'),
          'public class ExistingFeature { public int Value => 42; }\n');
      }
      const project = await readFile(path.join(cwd, 'MyApp.csproj'), 'utf8');
      assert.notEqual(project, originalProject);
      assert.match(project, /Name="CompileCalorFiles"/);
      assert.match(project, /<TargetFramework>net10\.0<\/TargetFramework>/);
      assert.match(await readFile(path.join(cwd, instructions[agent]), 'utf8'), /Calor/);
      const config = JSON.parse(await readFile(path.join(cwd, '.calor/config.json'), 'utf8'));
      assert.ok(config.agents.some(entry => entry.name === agent));
      // Use this checkout's runtime rather than requiring a globally installed tool.
      run(`dotnet build -p:CalorRuntimePath=${runtime}`);
      assert.equal(run('dotnet run --no-build'), existing ? 'Existing application' : 'Hello, World!');
    }
  }
  console.log('Published init commands passed for new and existing projects with all four agents; existing code preserved.');

  cwd = workspace;
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
  await writeFile(path.join(cwd, 'Program.calr'), source.replace('Hello from Calor!', 'Hello again!'));
  assert.equal(run(blocks(section('Try a change'))[0]), 'Hello again!');
  await writeFile(path.join(cwd, 'Program.calr'), source);

  const runReference = await readFile('content/cli/run.mdx', 'utf8');
  const [create, remove, compileAndRun] = blocks(section('Optional: inspect a .NET project', runReference));
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
