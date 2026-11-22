import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { basename, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const repo = fileURLToPath(new URL('../', import.meta.url));
const project = 'game/bots/Keyrun.Bots.Tests';
const projectFile = project + '/Keyrun.Bots.Tests.csproj';
const windows = process.platform === 'win32';
const dotnet = windows
  ? join(process.env.DOTNET_ROOT || join(process.env.USERPROFILE, '.dotnet'), 'dotnet.exe')
  : 'dotnet';

test('fastcheck reports the assertion, stack frame and retained TRX for each failed run', (t) => {
  mkdirSync(join(repo, 'build'), { recursive: true });
  const cwd = mkdtempSync(join(repo, 'build', 'fastcheck-test-'));
  t.after(() => rmSync(cwd, { recursive: true, force: true }));
  function run(command, args, expected = 0) {
    const result = spawnSync(command, args, { cwd, encoding: 'utf8' });
    if (result.error) throw result.error;
    const output = result.stdout + result.stderr;
    assert.equal(result.status, expected, output);
    return output;
  }

  mkdirSync(join(cwd, project), { recursive: true });
  mkdirSync(join(cwd, 'tools'));
  mkdirSync(join(cwd, 'node_modules'));
  copyFileSync(join(repo, 'tools/fastcheck.ps1'), join(cwd, 'tools/fastcheck.ps1'));
  writeFileSync(join(cwd, projectFile), `<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
    <ItemGroup>
      <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
      <PackageReference Include="xunit" Version="2.9.3" />
      <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    </ItemGroup>
  </Project>`);
  run(dotnet, ['new', 'sln', '-n', 'Keyrun', '-o', 'game']);
  run(dotnet, ['sln', 'game/Keyrun.sln', 'add', projectFile]);
  run('git', ['init', '-q']);
  run('git', ['add', '.']);
  run('git', ['-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid',
    'commit', '-qm', 'Fixture before test change']);
  writeFileSync(join(cwd, project, 'Probe.cs'), `using Xunit;
public class Probe {
    [Fact] public void Gate() => Assert.Fail("gate diagnostic marker");
    [Fact, Trait("Category", "Balance")] public void Balance() => Assert.Fail("balance diagnostic marker");
}`);

  // Only the unrelated lane gate is stubbed. The build and both filtered xUnit runs are real.
  const output = run(windows ? 'powershell' : 'pwsh', ['-NoProfile', '-ExecutionPolicy', 'Bypass',
    '-Command', "function npx { 'Tests 1 passed' }; & ./tools/fastcheck.ps1 -Base HEAD; exit $LASTEXITCODE"], 1);
  assert.match(output, /fastcheck: FAIL tests in game\/bots\/Keyrun.Bots.Tests/);
  for (const name of ['Gate', 'Balance']) {
    assert.ok(output.includes(name.toLowerCase() + ' diagnostic marker'), 'missing assertion message for ' + name);
    assert.ok(output.includes('at Probe.' + name + '()'), 'missing stack frame for ' + name);
  }
  const paths = [...output.matchAll(/Results File:\s*([^\r\n]+\.trx)\s*$/gmi)].map(match => resolve(cwd, match[1]));
  assert.equal(new Set(paths).size, 2, 'expected distinct retained TRX paths for gate and balance');
  for (const [index, name] of ['Gate', 'Balance'].entries()) {
    const trx = readFileSync(paths[index], 'utf8');
    assert.ok(trx.includes('testName="Probe.' + name + '"'), basename(paths[index]));
    assert.ok(trx.includes(name.toLowerCase() + ' diagnostic marker'), basename(paths[index]));
  }
});
