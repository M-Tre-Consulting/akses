using System.Text.Json.Nodes;
using Akses.Core;
using Akses.Core.Engines;

namespace Akses.Core.Tests;

public class FormatCatalogTests
{
    [Theory]
    [InlineData("foto.WEBP", FileKind.Image)]
    [InlineData("IMG_0001.heic", FileKind.Image)]
    [InlineData("clip.mkv", FileKind.Video)]
    [InlineData("song.flac", FileKind.Audio)]
    [InlineData("doc.pdf", FileKind.Pdf)]
    [InlineData("tesi.docx", FileKind.Document)]
    [InlineData("data.yml", FileKind.Data)]
    [InlineData("film.srt", FileKind.Subtitle)]
    [InlineData("setup.exe", FileKind.Unknown)]
    public void KindOf_maps_extensions(string file, FileKind kind) => Assert.Equal(kind, FormatCatalog.KindOf(file));

    [Fact]
    public void Animated_images_can_become_video() =>
        Assert.Contains(FormatCatalog.TargetsFor("anim.gif"), t => t.Id == "mp4");

    [Fact]
    public void Documents_do_not_offer_their_own_format()
    {
        Assert.DoesNotContain(FormatCatalog.TargetsFor("a.docx"), t => t.Id == "docx");
        Assert.DoesNotContain(FormatCatalog.TargetsFor("a.yml"), t => t.Id == "yaml");
        Assert.Contains(FormatCatalog.TargetsFor("a.jpg"), t => t.Id == "jpg"); // re-compress / resize
    }

    [Fact]
    public void Every_extension_has_targets() =>
        Assert.All(FormatCatalog.InputExtensions, ext => Assert.NotEmpty(FormatCatalog.TargetsFor("x." + ext)));
}

public class PdfPagesTests
{
    [Fact]
    public void Parses_ranges_and_singles() =>
        Assert.Equal([1, 2, 3, 5, 8, 9, 10], PdfEngine.ParsePages("1-3, 5, 8-", 10)!);

    [Fact]
    public void Reverse_range_and_duplicates() =>
        Assert.Equal([3, 2, 1], PdfEngine.ParsePages("3-1, 2", 5)!);

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("12")]
    public void Rejects_invalid(string text) => Assert.Null(PdfEngine.ParsePages(text, 10));
}

public class DataEngineTests
{
    [Fact]
    public void Csv_with_semicolons_and_quotes()
    {
        string csv = "﻿nome;città;note\nMario;Savona;\"ciao; \"\"amico\"\"\"\nAnna;Genova;007\n";
        Assert.Equal(';', DataEngine.DetectDelimiter(csv));
        var tree = (JsonArray)DataEngine.Read(csv, "csv")!;
        Assert.Equal(2, tree.Count);
        Assert.Equal("ciao; \"amico\"", (string)tree[0]!["note"]!);
        Assert.Equal("007", (string)tree[1]!["note"]!); // leading zero stays text
    }

    [Fact]
    public void Yaml_scalars_are_typed()
    {
        var tree = DataEngine.Read("a: 1\nb: true\nc: \"1\"\nd: ~\ne: [x, 2.5]\n", "yaml")!;
        Assert.Equal(1L, (long)tree["a"]!);
        Assert.True((bool)tree["b"]!);
        Assert.Equal("1", (string)tree["c"]!);
        Assert.Null(tree["d"]);
        Assert.Equal(2.5, (double)tree["e"]![1]!);
    }

    [Fact]
    public void Xml_attributes_and_repeated_elements()
    {
        var tree = DataEngine.Read("<lib><book id=\"1\">A</book><book id=\"2\">B</book></lib>", "xml")!;
        var books = (JsonArray)tree["lib"]!["book"]!;
        Assert.Equal(2, books.Count);
        Assert.Equal(2L, (long)books[1]!["@id"]!);
        Assert.Equal("B", (string)books[1]!["#text"]!);
    }
}

public class OutputPathsTests
{
    [Fact]
    public void Never_overwrites()
    {
        string dir = Directory.CreateTempSubdirectory("akses-test").FullName;
        try
        {
            string first = OutputPaths.Unique(dir, "foto", "png");
            Assert.EndsWith("foto.png", first);
            File.WriteAllText(first, "");
            Assert.EndsWith("foto (1).png", OutputPaths.Unique(dir, "foto", ".png"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
