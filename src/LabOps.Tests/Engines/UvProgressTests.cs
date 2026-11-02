using LabOps.Core.Engines;

namespace LabOps.Tests.Engines;

public sealed class UvProgressTests
{
    // Lines uv 0.12.23 wrote to stderr for a first `uv sync --frozen` in LabOps-Protocols, with no
    // Python and an empty cache. Setup shows them while the engine is prepared.
    [Theory]
    [InlineData("Downloading cpython-3.13.16-windows-x86_64-none (download) (21.0MiB)", "downloading Python (21.0 MiB)")]
    [InlineData(" Downloaded cpython-3.13.16-windows-x86_64-none (download)", "downloaded Python")]
    [InlineData("Creating virtual environment at: .venv", "creating the Python environment")]
    [InlineData("Downloading pillow (6.9MiB)", "downloading pillow (6.9 MiB)")]
    [InlineData(" Downloaded pillow", "downloaded pillow")]
    [InlineData("Prepared 17 packages in 1.23s", "installing 17 packages")]
    [InlineData("Installed 17 packages in 455ms", "installed 17 packages")]
    public void A_stage_of_uv_sync_is_described(string line, string expected) =>
        UvProgress.Describe(line).ShouldBe(expected);

    [Theory]
    [InlineData("Using CPython 3.13.16")]
    [InlineData(" + cryptography==50.0.2")]
    [InlineData("Checked 17 packages in 3ms")]
    [InlineData("")]
    public void A_line_that_is_not_a_stage_leaves_the_last_one_shown(string line) =>
        UvProgress.Describe(line).ShouldBeNull();
}
