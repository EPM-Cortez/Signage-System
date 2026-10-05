import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { mkdtemp, mkdir, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import pptxgen from 'pptxgenjs';

const delay = (milliseconds: number) => new Promise(resolveDelay => setTimeout(resolveDelay, milliseconds));

async function stopServer(child: ChildProcess): Promise<void> {
  if (child.exitCode !== null) return;

  const exited = new Promise<void>(resolveExit => child.once('exit', () => resolveExit()));
  child.kill();
  await Promise.race([exited, delay(5_000)]);
  if (child.exitCode !== null) return;

  if (process.platform === 'win32' && child.pid) {
    spawnSync('taskkill', ['/PID', child.pid.toString(), '/T', '/F'], { stdio: 'ignore', shell: false });
  } else {
    child.kill('SIGKILL');
  }
  await Promise.race([exited, delay(5_000)]);
}

async function waitForServer(child: ChildProcess): Promise<void> {
  const deadline = Date.now() + 120_000;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) throw new Error(`School Signage test server exited with code ${child.exitCode}.`);
    try {
      const response = await fetch('http://127.0.0.1:5189/health/live');
      if (response.ok) return;
    } catch {
      // The application is still starting.
    }
    await delay(250);
  }
  throw new Error('School Signage test server did not become healthy within 120 seconds.');
}

export default async function globalSetup(): Promise<() => Promise<void>> {
  const fixtureDirectory = resolve(import.meta.dirname, '../../../output/playwright/fixtures');
  await mkdir(fixtureDirectory, { recursive: true });
  const presentation = new pptxgen();
  presentation.layout = 'LAYOUT_WIDE';
  presentation.author = 'School Signage automated test';
  presentation.subject = 'Internally generated fixture';
  presentation.title = 'Playwright welcome';
  presentation.company = 'School Signage';
  const slide = presentation.addSlide();
  slide.background = { color: '0B2A4A' };
  slide.addShape(presentation.ShapeType.rect, { x: 0.5, y: 0.5, w: 12.3, h: 6.5, fill: { color: '0B2A4A' }, line: { color: '0B2A4A' } });
  slide.addText('Playwright welcome', { x: 1, y: 1.8, w: 11.3, h: 1, fontFace: 'Arial', fontSize: 36, bold: true, color: 'FFFFFF', align: 'center' });
  slide.addText('Generated locally for the School Signage test suite', { x: 1, y: 3, w: 11.3, h: 0.6, fontFace: 'Arial', fontSize: 20, color: 'B7DCF6', align: 'center' });
  await presentation.writeFile({ fileName: resolve(fixtureDirectory, 'playwright-welcome.pptx') });

  const repositoryRoot = resolve(import.meta.dirname, '../../..');
  const dataRoot = await mkdtemp(join(tmpdir(), 'school-signage-e2e-'));
  const localDotnet = process.platform === 'win32'
    ? join(repositoryRoot, '.dotnet', 'dotnet.exe')
    : join(repositoryRoot, '.dotnet', 'dotnet');
  const dotnet = process.env.SIGNAGE_DOTNET || localDotnet;
  const webAssembly = join(repositoryRoot, 'src', 'Signage.Web', 'bin', 'Debug', 'net10.0', 'Signage.Web.dll');
  const child = spawn(dotnet, [webAssembly, '--urls', 'http://127.0.0.1:5189'], {
    cwd: repositoryRoot,
    shell: false,
    stdio: 'inherit',
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ConnectionStrings__SignageDb: `Data Source=${join(dataRoot, 'signage.db')};Foreign Keys=True;Default Timeout=5`,
      Database__InstanceLockPath: join(dataRoot, 'signage.lock'),
      Storage__RootPath: join(dataRoot, 'content'),
      Storage__MinimumFreeBytes: '0',
      Storage__CleanupIntervalMinutes: '10080',
      Rendering__TempRoot: join(dataRoot, 'temp'),
      Rendering__ConverterEntryPoint: join(repositoryRoot, 'src', 'Signage.Converter', 'dist', 'cli.js'),
      Rendering__WorkingDirectory: join(repositoryRoot, 'src', 'Signage.Converter'),
      Rendering__FontDirectories__0: join(repositoryRoot, 'fonts'),
      Backup__RootPath: join(dataRoot, 'backups'),
      Authentication__Mode: 'Development'
    }
  });

  try {
    await waitForServer(child);
  } catch (error) {
    await stopServer(child);
    await rm(dataRoot, { recursive: true, force: true });
    throw error;
  }

  return async () => {
    await stopServer(child);
    await rm(dataRoot, { recursive: true, force: true });
  };
}
