$ErrorActionPreference = 'Stop'

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testSource = Join-Path $PSScriptRoot 'dist\focus-history-test.cs'
$testApp = Join-Path $PSScriptRoot 'dist\focus-history-test.exe'

@'
using System;
using System.IO;
using NowAndDoing;

internal static class FocusHistoryTest
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Main()
    {
        string path = Path.Combine(Path.GetTempPath(), "lock-in-history-test-" + Guid.NewGuid() + ".json");
        try
        {
            DateTime started = new DateTime(2026, 9, 28, 14, 30, 0, DateTimeKind.Utc);
            FocusHistory first = FocusHistory.Load(path);
            first.Start("calculus", started);
            first.Save();

            FocusHistory afterStart = FocusHistory.Load(path);
            Check(afterStart.Sessions.Count == 1, "start was not saved");
            Check(afterStart.Sessions[0].Task == "calculus", "task name changed");
            Check(!afterStart.Sessions[0].FinishedUtc.HasValue, "new session was already finished");

            afterStart.Finish(afterStart.Sessions[0], started.AddMinutes(42).AddSeconds(7));
            afterStart.Save();
            FocusHistory afterFinish = FocusHistory.Load(path);
            Check(afterFinish.Sessions[0].FinishedUtc.Value - afterFinish.Sessions[0].StartedUtc ==
                TimeSpan.FromMinutes(42) + TimeSpan.FromSeconds(7), "studied time was not saved");

            afterFinish.Start("probability", started.AddHours(1));
            afterFinish.Save();
            Check(FocusHistory.Load(path).Sessions.Count == 2, "earlier sessions were lost");
            Console.WriteLine("focus history saves start, finish, duration, and later sessions");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        }
    }
}
'@ | Set-Content -LiteralPath $testSource -Encoding UTF8

try {
    & $compiler /nologo "/out:$testApp" /reference:System.Web.Extensions.dll `
        (Join-Path $PSScriptRoot 'FocusHistory.cs') $testSource
    if ($LASTEXITCODE -ne 0) { throw 'focus history test build failed' }
    & $testApp
    if ($LASTEXITCODE -ne 0) { throw 'focus history test failed' }
}
finally {
    Remove-Item -LiteralPath $testSource, $testApp -ErrorAction SilentlyContinue
}
