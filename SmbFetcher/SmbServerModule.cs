using Unosquare.Swan;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Unosquare.Labs.EmbedIO.Constants;
using System;
using System.Text;
using System.Security.AccessControl;
using System.Collections.Generic;

namespace SmbFetcher {
  public class SmbServerModule : Unosquare.Labs.EmbedIO.WebModuleBase {
    /// <summary>
    /// The chunk size for sending files
    /// </summary>
    const int ChunkSize = 4096;
    const string Format = "MM/dd/yyyy h:mm:ss tt";
    UTF8Encoding utf8 = new UTF8Encoding();

    /// <summary>
    /// Initializes a new instance of the <see cref="SmbServerModule"/> class.
    /// </summary>
    /// <param name="basePath">The base path.</param>
    /// <param name="jsonPath">The json path.</param>
    public SmbServerModule() {
      AddHandler(Unosquare.Labs.EmbedIO.ModuleMap.AnyPath, HttpVerbs.Head, (context, ct) => HandleGet(context, ct, false));
      AddHandler(Unosquare.Labs.EmbedIO.ModuleMap.AnyPath, HttpVerbs.Get, (context, ct) => HandleGet(context, ct));

    }

    /// <summary>
    /// Gets the Module's name
    /// </summary>
    public override string Name => nameof(SmbServerModule).Humanize();

    async Task<bool> HandleGet(Unosquare.Labs.EmbedIO.IHttpContext context, CancellationToken ct, bool sendBuffer = true) {
      string action = context.Request.Headers["action"];
      string path = context.Request.Headers["path"];
      //Console.WriteLine("Request action={0}, path={1}", action, path);

      if (action == null) {
        context.Response.StatusCode = 200;
        return true;
      }

      if ("download".Equals(action)) {
        using (Stream inputStream = File.OpenRead(path)) {
          context.Response.ContentType = "application/octet-stream";
          context.Response.AddHeader(Headers.CacheControl, "no-cache");
          context.Response.AddHeader("Pragma", "no-cache");
          context.Response.AddHeader("Expires", "0");
          if (sendBuffer == false) {
            return true;
          }
          await WriteToOutputStream(context.Response, inputStream, ct);
        }
      } else if ("info".Equals(action)) {
        var responseSb = new StringBuilder();
        if (File.GetAttributes(path).HasFlag(FileAttributes.Directory)) {
          responseSb.AppendLine("directory");
          DirectoryInfo directoryInfo = new DirectoryInfo(path);

          foreach (DirectoryInfo child in directoryInfo.GetDirectories()) {
            responseSb.AppendLine("directory\t" + child.Name);
          }
          foreach (FileInfo child in directoryInfo.GetFiles()) {
            responseSb.Append("file\t" + child.Name);
            responseSb.Append("\t" + child.LastAccessTimeUtc.ToString(Format));
            responseSb.Append("\t" + child.LastWriteTimeUtc.ToString(Format));
            responseSb.AppendLine("\t" + child.CreationTimeUtc.ToString(Format));
          }
        } else {
          responseSb.AppendLine("file");
          FileInfo fileInfo = new FileInfo(path);
          FileSecurity fileSecurity = fileInfo.GetAccessControl();
          StringBuilder stringBuilder = new StringBuilder();
          foreach (FileSystemAccessRule fsar in fileSecurity.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier))) {
            if (stringBuilder.Length != 0) {
              stringBuilder.Append(",");
            }
            stringBuilder.Append(fsar.IdentityReference.Value);
          }
          responseSb.AppendLine(stringBuilder.ToString());
          responseSb.AppendLine(fileInfo.CreationTimeUtc.ToString(Format));
          responseSb.AppendLine(fileInfo.LastAccessTimeUtc.ToString(Format));
          responseSb.AppendLine(fileInfo.LastWriteTimeUtc.ToString(Format));
          responseSb.AppendLine(fileInfo.Length + "");
        }
        byte[] resBytes = utf8.GetBytes(responseSb.ToString());
        context.Response.ContentType = "text/plain";
        context.Response.OutputStream.Write(resBytes, 0, resBytes.Length);
        context.Response.StatusCode = 200;
      }

      return true;
    }

    static async Task WriteToOutputStream(
      Unosquare.Labs.EmbedIO.IHttpResponse response,
        Stream buffer,
        CancellationToken ct) {
      await buffer.CopyToAsync(response.OutputStream, ChunkSize, ct);
      response.OutputStream.Flush();
    }
  }
}