using SaSkinManager.Core;
using SaSkinManager.Core.Formats;
using SaSkinManager.Core.Game;
using Xunit;

// O idioma (L.Lang) é global: os testes não podem rodar em paralelo.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SaSkinManager.Core.Tests;

public class LocalizationTests
{
    [Fact]
    public void Catalog_texts_follow_the_language()
    {
        var ide = IdeFile.Parse(Fake.PedsIde);
        var models = PedCatalog.FromIde(ide);
        var male = models.First(m => m.ModelName == "male01");
        var lapd = models.First(m => m.ModelName == "lapd1");
        var bfyri = models.First(m => m.ModelName == "bfyri");
        var cs = PedCatalog.Special("csryder");
        try
        {
            L.Set(L.English);
            Assert.Equal("Pedestrians (men)", PedCatalog.CategoryLabel(male.Category));
            Assert.Equal("Los Santos police officer", PedCatalog.DisplayNameFor(lapd));
            Assert.Equal("Black young woman · ri", PedCatalog.DescriptionFor(bfyri));
            Assert.Equal("Ryder (cutscene)", PedCatalog.DisplayNameFor(cs));
            Assert.Equal("Folder not found.", GameInstall.Validate(@"Z:\nao\existe"));

            L.Set(L.Portuguese);
            Assert.Equal("Pedestres (homens)", PedCatalog.CategoryLabel(male.Category));
            Assert.Equal("Policial de Los Santos", PedCatalog.DisplayNameFor(lapd));
            Assert.Equal("Mulher jovem negra · ri", PedCatalog.DescriptionFor(bfyri));
            Assert.Equal("Pasta não encontrada.", GameInstall.Validate(@"Z:\nao\existe"));
        }
        finally
        {
            L.Set(L.Auto);
        }
    }

    [Fact]
    public void Finds_cutscene_and_mission_variants_of_story_characters()
    {
        var catalog = PedCatalog.FromIde(IdeFile.Parse(Fake.PedsIde));
        catalog.AddRange(new[] { "cssweet", "smoke", "cssmoke", "smokev", "csryder", "csbmydj" }.Select(n => PedCatalog.Special(n)));
        var sweet = catalog.First(m => m.ModelName == "sweet");
        var smoke = catalog.First(m => m.ModelName == "smoke");
        var male = catalog.First(m => m.ModelName == "male01");

        Assert.Equal(new[] { "cssweet" }, PedCatalog.LinkedModels(sweet, catalog).Select(m => m.ModelName));
        Assert.Equal(new[] { "cssmoke", "smokev" }, PedCatalog.LinkedModels(smoke, catalog).Select(m => m.ModelName));
        Assert.Empty(PedCatalog.LinkedModels(male, catalog)); // pedestre genérico nunca liga
    }

    [Fact]
    public void Thumbnail_prefers_face_texture()
    {
        var body = Fake.Txd8888("body", 64, 64, 1, 2, 3);
        var head = Fake.Txd8888("head", 32, 32, 4, 5, 6);
        // junta as duas texturas num TXD só
        var natives = new[] { body, head }.Select(t => t[(12 + 16)..]).ToArray(); // pula TexDictionary + Struct(4)
        var header = new byte[4];
        BitConverter.GetBytes((ushort)2).CopyTo(header, 0);
        BitConverter.GetBytes((ushort)2).CopyTo(header, 2);
        var txd = Fake.Chunk(RwChunk.TexDictionary, Fake.Chunk(RwChunk.Struct, header).Concat(natives.SelectMany(x => x)).ToArray());

        Assert.Equal(2, TxdReader.DecodeAll(txd).Count());
        Assert.Equal("head", TxdReader.DecodeBest(txd)!.Name);
        Assert.Equal("body", TxdReader.DecodeLargest(txd)!.Name);
    }
}
