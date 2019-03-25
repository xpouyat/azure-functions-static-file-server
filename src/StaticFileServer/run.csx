using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.IO;
using MimeTypes;
using Microsoft.WindowsAzure.MediaServices.Client;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System.Linq;

const string staticFilesFolder = "www";
static string defaultPage = string.IsNullOrEmpty(GetEnvironmentVariable("DEFAULT_PAGE")) ? 
    "index.html" : GetEnvironmentVariable("DEFAULT_PAGE");

static readonly string _AADTenantDomain = Environment.GetEnvironmentVariable("AMSAADTenantDomain");
static readonly string _RESTAPIEndpoint = Environment.GetEnvironmentVariable("AMSRESTAPIEndpoint");

static readonly string _mediaservicesClientId = Environment.GetEnvironmentVariable("AMSClientId");
static readonly string _mediaservicesClientSecret = Environment.GetEnvironmentVariable("AMSClientSecret");

// Field for service context.
private static CloudMediaContext _context = null;

public static HttpResponseMessage Run(HttpRequestMessage req, TraceWriter log)
{
    try
    {
        var filePath = GetFilePath(req, log);

        stream myStream = null;
        if (filePath.EndsWith("player.html"))
        {
            // Load AMS account context
            log.Info($"Using Azure Media Service Rest API Endpoint : {_RESTAPIEndpoint}");

            AzureAdTokenCredentials tokenCredentials = new AzureAdTokenCredentials(_AADTenantDomain,
                                  new AzureAdClientSymmetricKey(_mediaservicesClientId, _mediaservicesClientSecret),
                                  AzureEnvironments.AzureCloudEnvironment);

            AzureAdTokenProvider tokenProvider = new AzureAdTokenProvider(tokenCredentials);

            _context = new CloudMediaContext(new Uri(_RESTAPIEndpoint), tokenProvider);
            var channel = _context.Channels.Where(c => c.Name == "Channel1").FirstOrDefault();
            var program = channel.Programs.Where(p => p.Name == "Program1").FirstOrDefault();
            var asset  = program.Asset;
            var assetLocator = asset.Locators.FirstOrDefault();
            string url = "//" + _context.StreamingEndpoints.FirstOrDefault().HostName + "/" + assetLocator.Path + "/" + program.ManifestName + ".ism/manifest";
            log.Info($"Program URL: {url}");
            string data = File.ReadAllText(path).Replace("insertprogramurlhere", url);
            byte[] bytes = Encoding.ASCII.GetBytes(data);
            myStream = new MemoryStream(bytes);
        }
        else
        {
            myStream = new FileStream(filePath, FileMode.Open);
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StreamContent(myStream);
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(filePath));
        return response;
    }
    catch
    {
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}

private static string GetScriptPath()
    => Path.Combine(GetEnvironmentVariable("HOME"), @"site\wwwroot");

private static string GetEnvironmentVariable(string name)
    => System.Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);

private static string GetFilePath(HttpRequestMessage req, TraceWriter log)
{
    var pathValue = req.GetQueryNameValuePairs()
        .FirstOrDefault(q => string.Compare(q.Key, "file", true) == 0)
        .Value;

    var path = pathValue ?? "";
    
    var staticFilesPath = Path.GetFullPath(Path.Combine(GetScriptPath(), staticFilesFolder));
    var fullPath = Path.GetFullPath(Path.Combine(staticFilesPath, path));

    if (!IsInDirectory(staticFilesPath, fullPath))
    {
        throw new ArgumentException("Invalid path");
    }

    var isDirectory = Directory.Exists(fullPath);
    if (isDirectory)
    {
        fullPath = Path.Combine(fullPath, defaultPage);
    }

    return fullPath;
}

private static bool IsInDirectory(string parentPath, string childPath)
{
    var parent = new DirectoryInfo(parentPath);
    var child = new DirectoryInfo(childPath);

    var dir = child;
    do
    {
        if (dir.FullName == parent.FullName)
        {
            return true;
        }
        dir = dir.Parent;
    } while (dir != null);

    return false;
}

private static string GetMimeType(string filePath)
{
    var fileInfo = new FileInfo(filePath);
    return MimeTypeMap.GetMimeType(fileInfo.Extension);
}
