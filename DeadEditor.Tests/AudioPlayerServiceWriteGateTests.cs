using DeadEditor.Services;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Regression gate for the concurrent-save file lock: saving metadata could fail with
/// "the process cannot access the file ... because it is being used by another process"
/// when a second save (or a fingerprint / MBID write) entered its write action while the
/// first still held the FLAC open for exclusive write. The fix serializes every guarded
/// write window inside <see cref="AudioPlayerService.WithFileReleased"/> with a static
/// <c>SemaphoreSlim(1,1)</c>.
///
/// This test drives two writers at the same file through <c>WithFileReleased</c>
/// concurrently and asserts no <see cref="IOException"/>. Each writer opens the file with
/// <see cref="FileShare.None"/> — the same exclusive-write mode TagLib's
/// <c>Flac.File.Save</c> uses — and holds it briefly, so two un-serialized overlapping
/// opens reliably collide. With the gate in place they serialize and both succeed.
///
/// Pre-fix verification: temporarily removing the <c>_writeGate.Wait()/Release()</c> pair
/// in <c>WithFileReleased</c> makes this test fail (IOException collisions), confirming it
/// gates the actual bug.
/// </summary>
public class AudioPlayerServiceWriteGateTests
{
    /// <summary>
    /// Builds a fresh, isolated <see cref="AudioPlayerService"/> via its private singleton
    /// constructor. The constructor only initializes the playlist and state (no NAudio
    /// device, no media keys), and with no file loaded <c>WithFileReleased</c> takes its
    /// no-op branch (no Stop/Play), so the test exercises only the new write gate — the
    /// gate is <c>static</c>, so the isolated instance still shares it app-wide.
    /// </summary>
    private static AudioPlayerService NewIsolatedService()
    {
        var ctor = typeof(AudioPlayerService).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        Assert.NotNull(ctor);
        return (AudioPlayerService)ctor!.Invoke(null);
    }

    [Fact]
    public async Task WithFileReleased_ConcurrentWritersOnSameFile_DoNotCollide()
    {
        var tempPath = Path.Combine(
            Path.GetTempPath(), $"deadeditor_writegate_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(tempPath, new byte[] { 1, 2, 3, 4 });

        try
        {
            var svc = NewIsolatedService();
            var failures = new ConcurrentBag<Exception>();
            const int iterations = 25;

            // The paths passed do NOT match the (unloaded) current file, so WithFileReleased
            // runs the action through the gate without touching the NAudio player.
            void Writer()
            {
                for (int i = 0; i < iterations; i++)
                {
                    try
                    {
                        svc.WithFileReleased(new[] { tempPath }, () =>
                        {
                            using var fs = new FileStream(
                                tempPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                            // Widen the exclusive-hold window so an un-gated peer would collide.
                            Thread.Sleep(5);
                            fs.WriteByte(9);
                        });
                    }
                    catch (Exception ex)
                    {
                        failures.Add(ex);
                    }
                }
            }

            await Task.WhenAll(Task.Run(Writer), Task.Run(Writer));

            Assert.Empty(failures);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
