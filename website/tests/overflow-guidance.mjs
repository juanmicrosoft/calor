import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile, rm } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { randomUUID } from 'node:crypto';

const compiler = path.resolve('../src/Calor.Compiler/bin/Debug/net10.0/calor.dll');
const runtime = path.resolve('../src/Calor.Compiler/bin/Debug/net10.0/Calor.Runtime.dll');
const workspace = path.resolve('test-results', `overflow-guidance-${randomUUID()}`);
await mkdir(path.join(workspace, 'scratch'), { recursive: true });
const env = { ...process.env, CALOR_TELEMETRY: '0', DOTNET_NOLOGO: '1',
  TMPDIR: path.join(workspace, 'scratch'), TMP: path.join(workspace, 'scratch'),
  TEMP: path.join(workspace, 'scratch') };
function run(args) {
  const result = spawnSync('dotnet', args, { cwd: workspace, env, encoding: 'utf8', timeout: 120000 });
  assert.equal(result.status, 0, `${args.join(' ')}\n${result.error || ''}\n${result.stdout}\n${result.stderr}`);
  return result.stdout.replace(/\r/g, '').trim();
}
try {
  for (const file of ['Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props']) {
    await writeFile(path.join(workspace, file), '<Project />');
  }
  await writeFile(path.join(workspace, 'Probe.csproj'), `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup><Compile Include="Program.cs" /><Compile Include="Snippet.g.cs" />
    <Reference Include="Calor.Runtime"><HintPath>${runtime.replaceAll('&', '&amp;').replaceAll('<', '&lt;')}</HintPath></Reference>
  </ItemGroup>
</Project>`);
  await writeFile(path.join(workspace, 'Program.cs'), `
using System.Reflection;
var square = Assembly.GetExecutingAssembly().GetTypes()
    .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
    .Single(method => method.Name == "Square");
foreach (var value in new[] { 46340, 46341 })
{
    try { Console.WriteLine($"{value}:{square.Invoke(null, new object[] { value })}"); }
    catch (TargetInvocationException error) when (error.InnerException is OverflowException)
    { Console.WriteLine($"{value}:OverflowException"); }
}
`);
  const execute = () => run(['run', '--project', 'Probe.csproj', '--no-launch-profile']);
  for (const page of ['philosophy/static-verification', 'benchmarking/metrics/contract-verification']) {
    const doc = (await readFile(`content/${page}.mdx`, 'utf8')).replace(/\r/g, '');
    const example = [...doc.matchAll(/```calor\n([\s\S]*?)```/g)]
      .map(match => match[1]).find(source => /^§F\{[^}\n]*:Square:/.test(source));
    assert.ok(example, `Missing published Square example in ${page}`);
    await writeFile(path.join(workspace, 'Snippet.calr'),
      `§M{m1:DocsSquare}\n${example.trimEnd().split('\n').map(line => `  ${line}`).join('\n')}\n`);
    for (const mode of ['debug', 'off']) {
      for (const verify of [false, true]) {
        run([compiler, '--input', 'Snippet.calr', '--output', 'Snippet.g.cs',
          '--contract-mode', mode, ...(verify ? ['--verify'] : [])]);
        assert.equal(execute(), '46340:2147395600\n46341:OverflowException', `${page}, ${mode}, verify=${verify}`);
      }
    }
  }

  const csharp = 'public static class ImportedSquare { public static int Square(int x) => x * x; }';
  await writeFile(path.join(workspace, 'Snippet.g.cs'), csharp);
  const original = execute();
  assert.equal(original, '46340:2147395600\n46341:-2147479015');
  await writeFile(path.join(workspace, 'Original.cs'), csharp);
  run([compiler, 'convert', 'Original.cs', '--output', 'Imported.calr', '--validate']);
  assert.match(await readFile(path.join(workspace, 'Imported.calr'), 'utf8'), /overflow=unchecked/);
  run([compiler, '--input', 'Imported.calr', '--output', 'Snippet.g.cs']);
  assert.equal(execute(), original);
  console.log('Published Square examples trap in debug/off with verification on/off; converted C# preserves unchecked results.');

  const comparison = await readFile('src/components/landing/CodeComparison.tsx', 'utf8');
  const calorFragment = comparison.match(/const calorCode = `([\s\S]*?)`;/)?.[1];
  const csharpFragment = comparison.match(/const csharpCode = `([\s\S]*?)`;/)?.[1];
  assert.ok(calorFragment && csharpFragment, 'Missing homepage comparison fragments');
  await writeFile(path.join(workspace, 'Program.cs'), `
using System.Reflection;
var square = Assembly.GetExecutingAssembly().GetTypes()
    .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
    .Single(method => method.Name == "Square");
foreach (var value in new[] { 0, 3, -1, 46340, 46341 })
{
    try { Console.WriteLine($"{value}:{square.Invoke(null, new object[] { value })}"); }
    catch (TargetInvocationException error) when (value == -1 && error.InnerException != null)
    { Console.WriteLine("-1:Rejected"); }
    catch (TargetInvocationException error) when (error.InnerException is OverflowException)
    { Console.WriteLine($"{value}:OverflowException"); }
}
`);
  const expected = '0:0\n3:9\n-1:Rejected\n46340:2147395600\n46341:OverflowException';
  await writeFile(path.join(workspace, 'Snippet.calr'), calorFragment);
  run([compiler, '--input', 'Snippet.calr', '--output', 'Snippet.g.cs']);
  assert.equal(execute(), expected);
  await writeFile(path.join(workspace, 'Snippet.g.cs'),
    `using System; public static class ComparisonSquare { ${csharpFragment} }`);
  assert.equal(execute(), expected);
  console.log('Homepage Calor/C# fragments agree on valid results, negative-input rejection and checked overflow.');
} finally {
  await rm(workspace, { recursive: true, force: true });
}
