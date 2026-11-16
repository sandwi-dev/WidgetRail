using System.Text;
using System.Text.Json;
using WidgetRail.Playnite;

namespace WidgetRail.Playnite.Tests;

[TestClass]
public sealed class CaptureCompanionTests
{
    [TestMethod]
    public void RelatedIdentityIsProfileScopedAndOrdinal()
    {
        var profile=Guid.NewGuid().ToString("D"); var game=Guid.NewGuid();
        var id=CaptureRelatedIdentity.Achievement(profile,game,"steam","API_NAME");
        Assert.IsTrue(CaptureRelatedIdentity.TryGame(profile,CaptureRelatedIdentity.AchievementKind,id,out var resolved));
        Assert.AreEqual(game,resolved);
        Assert.IsFalse(CaptureRelatedIdentity.TryGame(Guid.NewGuid().ToString("D"),CaptureRelatedIdentity.AchievementKind,id,out _));
        Assert.AreNotEqual(id,CaptureRelatedIdentity.Achievement(profile,game,"Steam","API_NAME"));
        Assert.AreNotEqual(id,CaptureRelatedIdentity.Achievement(profile,game,"steam","api_name"));
        Assert.IsFalse(CaptureRelatedIdentity.TryGame(profile,CaptureRelatedIdentity.GameKind,id,out _));
    }
    [TestMethod]
    public void ProviderNamesMatchOnlyKnownShapeAndUniqueCandidates()
    {
        string[] suffixes=["clean","notification","framed"];
        Assert.AreEqual("An achievement",ProviderCaptureCompanions.AchievementStem("003_An achievement_clean (2).png",suffixes));
        Assert.AreEqual("An achievement",ProviderCaptureCompanions.AchievementStem("003_An achievement_notification.mp4",suffixes));
        Assert.IsNull(ProviderCaptureCompanions.AchievementStem("Screenshot.png",suffixes));
        Assert.IsNull(ProviderCaptureCompanions.AchievementStem("003_Achievement.exe",suffixes));
        Assert.IsNull(ProviderCaptureCompanions.AchievementStem("003_Achievement_old.png",suffixes),"Unknown historical suffixes must not guess another achievement.");
        CollectionAssert.AreEquivalent(new[]{"Name","Name_with"},ProviderCaptureCompanions.AchievementStems("003_Name_with_toast.png",new[]{"toast","with_toast"}).ToArray());
        CollectionAssert.AreEquivalent(new[]{"Name","Name_clean"},ProviderCaptureCompanions.AchievementStems("003_Name_clean.png",new[]{"","clean"}).ToArray());
        Assert.AreEqual("Game Name",ProviderCaptureCompanions.Segment("Game: Name",game:true));
        Assert.AreEqual("Achievement_Name",ProviderCaptureCompanions.Segment("Achievement:Name"));
        Assert.IsNull(ProviderCaptureCompanions.Unique(new[]{"same","same"}));
        Assert.AreEqual("only",ProviderCaptureCompanions.Unique(new[]{"only"}));
    }
    [TestMethod]
    public void CompanionIsAtomicClosedMetadataAndNeverReplacesOriginalOrExistingAssociation()
    {
        var root=Path.Combine(Path.GetTempPath(),"widgetrail-companion-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file=Path.Combine(root,"003_Test_clean.png"); byte[] bytes=[1,2,3,4]; File.WriteAllBytes(file,bytes);
            var originalAttributes=File.GetAttributes(file);
            var context=new ProviderCaptureContext {IdempotencyKey="artifact-key", References=[new(){Role="Subject",Kind=CaptureRelatedIdentity.GameKind,Id="game-id",Label="Game"}]};
            Assert.IsTrue(ProviderCaptureCompanions.Write(root,file,context));
            CollectionAssert.AreEqual(bytes,File.ReadAllBytes(file));
            Assert.IsTrue(File.GetAttributes(file+ProviderCaptureCompanions.Suffix).HasFlag(FileAttributes.Hidden),"New companion is hidden before publication.");
            Assert.AreEqual(originalAttributes,File.GetAttributes(file),"Original attributes are untouched.");
            var companion=File.ReadAllBytes(file+ProviderCaptureCompanions.Suffix);
            using var json=JsonDocument.Parse(companion);
            Assert.AreEqual("artifact-key",json.RootElement.GetProperty("idempotencyKey").GetString());
            Assert.AreEqual("Subject",json.RootElement.GetProperty("references")[0].GetProperty("role").GetString());
            Assert.IsFalse(json.RootElement.GetProperty("references")[0].TryGetProperty("hasRelatedAction",out _),"Generic metadata has no action by default.");
            Assert.IsFalse(json.RootElement.TryGetProperty("origin",out _));
            context.IdempotencyKey="replacement";
            Assert.IsFalse(ProviderCaptureCompanions.Write(root,file,context));
            CollectionAssert.AreEqual(companion,File.ReadAllBytes(file+ProviderCaptureCompanions.Suffix));
            context.IdempotencyKey="artifact-key";
            context.References[0].Label="Renamed game";
            context.References[0].HasRelatedAction=true;
            var unhidden=File.GetAttributes(file+ProviderCaptureCompanions.Suffix)&~FileAttributes.Hidden;
            File.SetAttributes(file+ProviderCaptureCompanions.Suffix,unhidden);
            Assert.IsTrue(ProviderCaptureCompanions.Write(root,file,context));
            Assert.AreEqual(unhidden,File.GetAttributes(file+ProviderCaptureCompanions.Suffix),"A deliberate unhide survives atomic replacement.");
            Assert.AreEqual(originalAttributes,File.GetAttributes(file));
            using var actionable=JsonDocument.Parse(File.ReadAllBytes(file+ProviderCaptureCompanions.Suffix));
            Assert.IsTrue(actionable.RootElement.GetProperty("references")[0].GetProperty("hasRelatedAction").GetBoolean());
            CollectionAssert.AreEqual(bytes,File.ReadAllBytes(file));
            Assert.AreEqual(0,Directory.GetFiles(root,"*.tmp").Length);
        }
        finally
        {
            var parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(root).StartsWith(parent,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(root).StartsWith("widgetrail-companion-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected fixture directory.");
            Directory.Delete(root,true);
        }
    }
}
