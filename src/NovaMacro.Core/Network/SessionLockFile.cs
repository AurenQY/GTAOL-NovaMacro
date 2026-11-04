using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NovaMacro.Core.Game;

namespace NovaMacro.Core.Network;

/// <summary>
/// 战局锁 — writes "x64\data\startup.meta" into the game folder with the password embedded as a
/// comment. Players with the same file/password are matched into the same private public session.
/// Templates are the ones shipped by the original tool.
/// </summary>
public static partial class SessionLockFile
{
    public const string RelativePath = @"x64\data\startup.meta";

    private const string EnhancedTemplate =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
        "<CDataFileMgr__ContentsOfDataFileXml>\r\n" +
        "\t<disabledFiles />\r\n" +
        "\t<includedXmlFiles itemType=\"CDataFileMgr__DataFileArray\" />\r\n" +
        "\t<includedDataFiles />\r\n" +
        "\t<dataFiles itemType=\"CDataFileMgr__DataFile\">\r\n" +
        "\t\t<Item>\r\n" +
        "\t\t\t<filename>platform:/data/cdimages/scaleform_platform_pc.rpf</filename>\r\n" +
        "\t\t\t<fileType>RPF_FILE</fileType>\r\n" +
        "\t\t</Item>\r\n" +
        "\t\t<Item>\r\n" +
        "\t\t<filename>platform:/data/cdimages/scaleform_frontend.rpf</filename>\r\n" +
        "\t\t\t<fileType>RPF_FILE_PRE_INSTALL</fileType>\r\n" +
        "\t\t</Item>\r\n" +
        "\t\t<Item>\r\n" +
        "\t\t<filename>platform:/data/cdimages/scaleform_frontend_gen9.rpf</filename>\r\n" +
        "\t\t\t<fileType>RPF_FILE_PRE_INSTALL</fileType>\r\n" +
        "\t\t</Item>\r\n" +
        "\t\t<Item>\r\n" +
        "\t\t<filename>platform:/levels/gta5/script/script.rpf</filename>\r\n" +
        "\t\t\t<fileType>RPF_FILE_PRE_INSTALL</fileType>\r\n" +
        "\t\t</Item>\r\n" +
        "\t</dataFiles>\r\n" +
        "\t<contentChangeSets itemType=\"CDataFileMgr__ContentChangeSet\" />\r\n" +
        "\t<dataFiles itemType=\"CDataFileMgr__DataFile\" />\r\n" +
        "\t<patchFiles />\r\n" +
        "</CDataFileMgr__ContentsOfDataFileXml>\r\n" +
        "<!--[密码]-->";

    private const string LegacyTemplate =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
        "<!--[密码]-->\r\n" +
        "<CDataFileMgr__ContentsOfDataFileXml>\r\n" +
        "\t<disabledFiles />\r\n" +
        "\t<includedXmlFiles itemType=\"CDataFileMgr__DataFileArray\" />\r\n" +
        "\t<includedDataFiles />\r\n" +
        "\t<dataFiles itemType=\"CDataFileMgr__DataFile\">\r\n" +
        "\t  <Item>\r\n" +
        "\t   <filename>platform:/data/cdimages/scaleform_platform_pc.rpf</filename>\r\n" +
        "\t   <fileType>RPF_FILE</fileType>\r\n" +
        "\t  </Item>\r\n" +
        "\t  <Item>\r\n" +
        "\t   <filename>platform:/data/cdimages/scaleform_frontend.rpf</filename>\r\n" +
        "\t   <fileType>RPF_FILE_PRE_INSTALL</fileType>\r\n" +
        "\t  </Item>\r\n" +
        "\t </dataFiles>\r\n" +
        "\t<contentChangeSets itemType=\"CDataFileMgr__ContentChangeSet\" />\r\n" +
        "\t<dataFiles itemType=\"CDataFileMgr__DataFile\" />\r\n" +
        "\t<patchFiles />\r\n" +
        "</CDataFileMgr__ContentsOfDataFileXml>                     \r\n" +
        "<!--[密码]-->";

    public static string Build(string password, GtaEdition edition)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("请输入密码！", nameof(password));
        }

        if (password.Contains("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("密码不能包含 “--”", nameof(password));
        }

        var template = edition == GtaEdition.Legacy ? LegacyTemplate : EnhancedTemplate;
        return template.Replace("[密码]", password.Trim(), StringComparison.Ordinal);
    }

    /// <summary>Returns the password stored in a startup.meta written by this tool (or the original), or null.</summary>
    public static string? ReadPassword(string content)
    {
        var m = CommentRegex().Match(content);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    public static string PathIn(string gameFolder) => Path.Combine(gameFolder, "x64", "data", "startup.meta");

    public static void Write(string gameFolder, string password, GtaEdition edition)
    {
        var path = PathIn(gameFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Build(password, edition), new UTF8Encoding(false));
    }

    public static string? Read(string gameFolder)
    {
        var path = PathIn(gameFolder);
        return File.Exists(path) ? ReadPassword(File.ReadAllText(path)) : null;
    }

    public static bool Remove(string gameFolder)
    {
        var path = PathIn(gameFolder);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    public static string RandomPassword(int length)
    {
        length = Math.Clamp(length, 1, 100);
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            sb.Append(alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]);
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"<!--(?!\[密码\])(.*?)-->", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();
}
