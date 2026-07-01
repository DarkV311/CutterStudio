using CutterStudio.Models;
using CutterStudio.Services;
using Xunit;

namespace CutterStudio.Tests;

public sealed class VectorAndHpglTests
{
    private const string SampleSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="100mm" height="50mm" viewBox="0 0 100 50">
          <rect x="5" y="5" width="90" height="40"/>
          <path d="M10,25 L90,25"/>
        </svg>
        """;

    [Fact]
    public void SvgParser_NormalizesDimensionsToMillimeters()
    {
        var service = new VectorArtworkService();
        var document = service.ParseSvg(SampleSvg, "Sample");

        Assert.Equal(2, document.Geometries.Count);
        Assert.InRange(document.BoundsMm.Width, 89.99, 90.01);
        Assert.InRange(document.BoundsMm.Height, 39.99, 40.01);
    }

    [Fact]
    public void HpglGenerator_ProducesInitializedPenCommandsAndPasses()
    {
        var vectorService = new VectorArtworkService();
        var document = vectorService.ParseSvg(SampleSvg);
        var settings = new CutterSettings
        {
            CutterProfile = "Generic HPGL",
            FlowControl = "None",
            Speed = 25,
            Pressure = 90,
            Passes = 2,
            Copies = 2,
            MaterialWidthMm = 250,
            WeedBorder = true
        };

        var job = new HpglService().Generate(document, settings);

        Assert.StartsWith("IN;VS25;FS90;PA;", job.Commands);
        Assert.Contains("PU", job.Commands);
        Assert.Contains("PD", job.Commands);
        Assert.EndsWith("PU0,0;", job.Commands);
        Assert.True(job.CuttingDistanceMm > 0);
        Assert.True(job.EstimatedCutDuration > TimeSpan.Zero);
        Assert.True(job.PenLifts > 0);
    }

    [Fact]
    public void BascocutProfile_ProducesDmplCommands()
    {
        var document = new VectorArtworkService().ParseSvg(SampleSvg);
        var settings = new CutterSettings
        {
            CutterProfile = "Bascocut CCD Tool 2 (DMPL)",
            BaudRate = 38400,
            FlowControl = "RTS/CTS",
            Speed = 20,
            Pressure = 58
        };

        var job = new HpglService().Generate(document, settings);

        Assert.StartsWith(";:H A L0 ECN U U0,0;U0,0;FS58;VS20;", job.Commands);
        Assert.Contains("U", job.Commands);
        Assert.Contains("D", job.Commands);
        Assert.DoesNotContain("PU", job.Commands);
        Assert.EndsWith("U0,0;@;", job.Commands);
    }

    [Fact]
    public void HpglGenerator_RejectsArtworkWiderThanMaterial()
    {
        var document = new VectorArtworkService().ParseSvg(SampleSvg);
        var settings = new CutterSettings { MaterialWidthMm = 20 };

        var exception = Assert.Throws<InvalidOperationException>(
            () => new HpglService().Generate(document, settings));

        Assert.Contains("material width", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CutPreview_AppliesCopiesRotationAndMaterialWidth()
    {
        var document = new VectorArtworkService().ParseSvg(SampleSvg);
        var settings = new CutterSettings
        {
            MaterialWidthMm = 300,
            Copies = 3,
            Rotate90 = true,
            WeedBorder = true,
            WeedBorderMarginMm = 2
        };

        var preview = new CutLayoutService().CreatePreview(document, settings);

        Assert.Equal(300, preview.BoundsMm.Width);
        Assert.True(preview.Geometries.Count >= 6);
        Assert.True(preview.BoundsMm.Height > 0);
        Assert.NotNull(preview.MaterialBoundsMm);
        Assert.NotNull(preview.ArtworkBoundsMm);
    }

    [Fact]
    public void PlacementOffset_MovesPreviewAndCutCoordinates()
    {
        var document = new VectorArtworkService().ParseSvg(SampleSvg);
        var settings = new CutterSettings
        {
            CutterProfile = "Bascocut CCD Tool 2 (DMPL)",
            MaterialWidthMm = 300,
            OffsetXmm = 20,
            OffsetYmm = 15
        };

        var preview = new CutLayoutService().CreatePreview(document, settings);
        var job = new HpglService().Generate(document, settings);

        Assert.True(preview.ArtworkBoundsMm!.Value.Left >= 19.99);
        Assert.True(preview.ArtworkBoundsMm.Value.Top >= 14.99);
        Assert.Contains("U800,600;", job.Commands);
    }

    [Fact]
    public void Merge_KeepsCurrentArtworkAndAddsInsertedProject()
    {
        var service = new VectorArtworkService();
        var current = service.ParseSvg(SampleSvg, "Current");
        var inserted = service.ParseSvg(
            """
            <svg xmlns="http://www.w3.org/2000/svg" width="20mm" height="20mm" viewBox="0 0 20 20">
              <circle cx="10" cy="10" r="8"/>
            </svg>
            """,
            "Inserted");

        var merged = service.Merge(current, inserted);

        Assert.Equal(current.Geometries.Count + inserted.Geometries.Count, merged.Geometries.Count);
        Assert.Equal("Current", merged.SourceName);
        Assert.Contains("<path", merged.SvgData);
    }

    [Fact]
    public void CutterProfiles_ContainRefineH721FamilyDefaults()
    {
        var profiles = new CutterProfileService();
        var refine = profiles.Get("Refine MH/EH 721");
        var mk2 = profiles.Get("Refine MH 721 MK2 (4800)");

        Assert.Equal("HPGL", refine.Protocol);
        Assert.Equal(9600, refine.BaudRate);
        Assert.Equal("RTS/CTS", refine.FlowControl);
        Assert.Equal(39.5, refine.UnitsPerMm);
        Assert.Equal(4800, mk2.BaudRate);
        Assert.Equal(630, refine.DefaultWidthMm);
    }

    [Fact]
    public void LayoutMetrics_ReturnMinimumVinylPieceWithSafetyMargin()
    {
        var document = new VectorArtworkService().ParseSvg(SampleSvg);
        var settings = new CutterSettings
        {
            MaterialWidthMm = 390,
            VinylMarginMm = 5,
            Copies = 1
        };

        var metrics = new CutLayoutService().CalculateMetrics(document, settings);

        Assert.InRange(metrics.ArtworkWidthMm, 89.99, 90.01);
        Assert.InRange(metrics.ArtworkHeightMm, 39.99, 40.01);
        Assert.InRange(metrics.VinylWidthMm, 99.99, 100.01);
        Assert.InRange(metrics.VinylLengthMm, 49.99, 50.01);
    }

    [Fact]
    public void AreaTest_BladeUpUsesOnlyPenUpMoves()
    {
        var document = new VectorArtworkService().ParseSvg(SampleSvg);
        var settings = new CutterSettings
        {
            CutterProfile = "Bascocut CCD Tool 2 (DMPL)",
            MaterialWidthMm = 390
        };

        var job = new HpglService().GenerateAreaTest(document, settings, AreaTestMode.BladeUp);

        Assert.Contains("U", job.Commands);
        Assert.DoesNotContain(";D", job.Commands);
        Assert.Equal(0, job.CuttingDistanceMm);
    }

    [Fact]
    public void AreaTest_BladeDownCutsBoundary()
    {
        var document = new VectorArtworkService().ParseSvg(SampleSvg);
        var settings = new CutterSettings
        {
            CutterProfile = "Generic HPGL",
            MaterialWidthMm = 390
        };

        var job = new HpglService().GenerateAreaTest(document, settings, AreaTestMode.BladeDown);

        Assert.Contains("PD", job.Commands);
        Assert.True(job.CuttingDistanceMm > 0);
    }

    [Fact]
    public void PrintCutSvg_IncludesRegistrationMarks()
    {
        var document = new VectorArtworkService().ParseSvg(SampleSvg);
        var svg = new PrintCutService().GeneratePrintableSvg(document, new CutterSettings());

        Assert.Contains("registration-marks", svg);
        Assert.Contains("<rect", svg);
        Assert.Contains("Cutter Studio Print", svg);
    }

    [Fact]
    public void PrintCutSvg_CanUseCircleCrossAndContourBox()
    {
        var document = new VectorArtworkService().ParseSvg(SampleSvg);
        var svg = new PrintCutService().GeneratePrintableSvg(document, new CutterSettings
        {
            RegistrationMarkStyle = RegistrationMarkStyle.CircleCross,
            CutContourBox = true,
            ContourGapMm = 2
        });

        Assert.Contains("<circle", svg);
        Assert.Contains("cut-contour-box", svg);
    }

    [Fact]
    public void ContourCorrection_ShiftsCutCoordinates()
    {
        var document = new VectorArtworkService().ParseSvg(SampleSvg);
        var settings = new CutterSettings
        {
            CutterProfile = "Bascocut CCD Tool 2 (DMPL)",
            MaterialWidthMm = 390,
            ContourCorrectionEnabled = true,
            ContourOffsetXmm = 10,
            ContourOffsetYmm = 5
        };

        var job = new HpglService().Generate(document, settings);

        Assert.Contains("U400,200;", job.Commands);
    }

    [Fact]
    public void UpdateService_HandlesGitHubVersionTags()
    {
        var service = new LicenseUpdateService();

        Assert.True(service.IsNewerVersion("v9.9.9"));
        Assert.False(service.IsNewerVersion("v0.0.1"));
    }

    [Fact]
    public void UpdateService_ResolvesRelativeManifestDownloadUrl()
    {
        var service = new LicenseUpdateService();
        var settings = new CutterSettings
        {
            UpdateSource = UpdateSourceKind.DirectManifest,
            DirectManifestUrl = "https://example.com/releases/latest.json"
        };
        var release = new LatestReleaseResponse(
            true,
            "9.9.9",
            "stable",
            "CutterStudio.zip",
            "",
            "",
            DateTime.UtcNow);

        var url = service.ResolveDownloadUrl(release, settings);

        Assert.Equal("https://example.com/releases/CutterStudio.zip", url);
    }
}
