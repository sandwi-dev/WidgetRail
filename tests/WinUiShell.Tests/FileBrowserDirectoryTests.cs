using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WinUiShell.Tests;

[TestClass]
public sealed class FileBrowserDirectoryTests
{
    [TestMethod]
    public void NewFolderRequiresAValidUniqueChildName()
    {
        var root = Directory.CreateTempSubdirectory("wr-create-folder-");
        try
        {
            var path = FileBrowserDirectory.CreateFolder(root.FullName, "Game captures");
            Assert.IsTrue(Directory.Exists(path));
            Assert.ThrowsExactly<IOException>(() => FileBrowserDirectory.CreateFolder(root.FullName, "game captures"));
            foreach (var invalid in new[] { "", ".", "..", @"..\outside", "a/b", "name.", "NUL", "a:b" })
                Assert.ThrowsExactly<ArgumentException>(() => FileBrowserDirectory.CreateFolder(root.FullName, invalid));
            Assert.AreEqual(1, root.EnumerateFileSystemInfos().Count());
        }
        finally { root.Delete(true); }
    }

    [TestMethod]
    public void NewFolderExplainsWriteDenialWithoutCreatingAnEntry()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("wr-folder-access-");
        var original = root.GetAccessControl();
        try
        {
            var acl = root.GetAccessControl();
            using var current = System.Security.Principal.WindowsIdentity.GetCurrent();
            acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(current.User!,
                System.Security.AccessControl.FileSystemRights.CreateDirectories, System.Security.AccessControl.AccessControlType.Deny));
            root.SetAccessControl(acl);
            var error = Assert.ThrowsExactly<UnauthorizedAccessException>(() => FileBrowserDirectory.CreateFolder(root.FullName, "Game captures"));
            StringAssert.Contains(error.Message, "permission");
            Assert.IsFalse(root.EnumerateFileSystemInfos().Any());
        }
        finally
        {
            root.SetAccessControl(original);
            root.Delete();
        }
    }

    [TestMethod]
    public async Task SearchIsFolderLocalAndTypedPagesAreBoundedAndStable()
    {
        var root=Directory.CreateTempSubdirectory("wr-files-");
        try
        {
            var folder=root.CreateSubdirectory("Zero matches");
            await File.WriteAllTextAsync(Path.Combine(folder.FullName,"nested.png"),"synthetic");
            for(var i=0;i<280;i++)File.WriteAllText(Path.Combine(root.FullName,$"Capture {i:000}.PNG"),"x");
            File.WriteAllText(Path.Combine(root.FullName,"package.zip"),"x");
            var request=new FileBrowserRequest(FileBrowserMode.Multiple,"Attach","Attach",root.FullName,".png",Limit:16);
            var all=new List<FileBrowserItem>();
            FileBrowserItem? after=null;
            for(var i=0;i<4;i++)
            {
                var batch=await FileBrowserDirectory.ReadAsync(root.FullName,request,"",after,default);
                Assert.IsLessThanOrEqualTo(FileBrowserDirectory.PageSize,batch.Items.Length);
                all.AddRange(batch.Items); if(!batch.HasMore)break; after=batch.Items[^1];
            }
            Assert.IsTrue(all[0].Folder);
            Assert.AreEqual(281,all.DistinctBy(row=>row.Path).Count());
            var filtered=await FileBrowserDirectory.ReadAsync(root.FullName,request,"re 27",null,default);
            Assert.AreEqual(10,filtered.Items.Length);
            Assert.AreEqual(0,(await FileBrowserDirectory.ReadAsync(root.FullName,request,"nested",null,default)).Items.Length);
            Assert.AreEqual(1,(await FileBrowserDirectory.ReadAsync(root.FullName,request with{Mode=FileBrowserMode.Folder},"",null,default)).Items.Length);
            var videoOnly=await FileBrowserDirectory.ReadAsync(root.FullName,request with{Extension=null},"",null,default,FileBrowserFilter.Videos);
            Assert.IsTrue(videoOnly.Items is [{Folder:true}]);
            using var cancelled=new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsExactlyAsync<TaskCanceledException>(()=>FileBrowserDirectory.ReadAsync(root.FullName,request,"",null,cancelled.Token));
        }
        finally {root.Delete(true);}
    }

    [TestMethod]
    public void LocationAndSaveNameStayWithinTheSelectedDestination()
    {
        Assert.AreEqual(@"C:\My documents\音楽",FileBrowserDirectory.FullPath("\"C:\\My documents\\音楽\""));
        Assert.AreEqual(@"\\server\share\Videos",FileBrowserDirectory.FullPath(@"\\server\share\Videos"));
        foreach(var invalid in new[]{@"C:relative",@"..\parent",@"\\.\pipe\demo",@"\\?\GLOBALROOT\device",@"C:\a:stream"})
            Assert.ThrowsExactly<ArgumentException>(()=>FileBrowserDirectory.FullPath(invalid));
        Assert.AreEqual(@"C:\Temp\clip.mp4",FileBrowserDirectory.SavePath(@"C:\Temp","clip",".mp4"));
        var longestStem = new string('a', 251);
        Assert.AreEqual(longestStem + ".mp4", Path.GetFileName(FileBrowserDirectory.SavePath(@"C:\Temp", longestStem, ".mp4")));
        Assert.ThrowsExactly<ArgumentException>(() => FileBrowserDirectory.SavePath(@"C:\Temp", longestStem + "a", ".mp4"));
        foreach(var invalid in new[]{@"..\clip.mp4","name. ","NUL.mp4","COM1.mp4","hello.mp4:stream","wrong.gif"})
            Assert.ThrowsExactly<ArgumentException>(()=>FileBrowserDirectory.SavePath(@"C:\Temp",invalid,".mp4"));
    }
}
