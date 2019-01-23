using Unosquare.Swan;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Unosquare.Labs.EmbedIO.Constants;
using System;
using System.Text;
using System.Collections.Generic;
using SMBLibrary.Client;
using SMBLibrary;

namespace SmbFetcher {
  public class SmbServerModule : Unosquare.Labs.EmbedIO.WebModuleBase, IDisposable {
    /// <summary>
    /// The chunk size for sending files
    /// </summary>
    const int ChunkSize = 4096;
    const string Format = "MM/dd/yyyy h:mm:ss tt";
    UTF8Encoding utf8 = new UTF8Encoding();
    SMB2Client smb;
    string share;

    /// <summary>
    /// Initializes a new instance of the <see cref="SmbServerModule"/> class.
    /// </summary>
    /// <param name="basePath">The base path.</param>
    /// <param name="jsonPath">The json path.</param>
    public SmbServerModule(string host, string domain, string username, string password, string share) {
      AddHandler(Unosquare.Labs.EmbedIO.ModuleMap.AnyPath, HttpVerbs.Head, (context, ct) => HandleGet(context, ct, false));
      AddHandler(Unosquare.Labs.EmbedIO.ModuleMap.AnyPath, HttpVerbs.Get, (context, ct) => HandleGet(context, ct));
      smb = new SMB2Client();
      if (!smb.Connect(System.Net.IPAddress.Parse(host), SMBTransportType.DirectTCPTransport)) {
        Console.WriteLine("Error connecting.");
      }
      var status = smb.Login(domain, username, password);
      if (status != NTStatus.STATUS_SUCCESS) {
        throw new Exception("Could not connect to smb server. Status: " + status);
      }
      this.share = share;
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
        NTStatus status;
        SMB2FileStore tree = smb.TreeConnect(share, out status) as SMB2FileStore;
        if (status == NTStatus.STATUS_SUCCESS) {
          object handle;
          FileStatus fs;
          status = tree.CreateFile(out handle, out fs, path == null ? "" : path, AccessMask.GENERIC_READ, 0, ShareAccess.Read, CreateDisposition.FILE_OPEN,
                                   CreateOptions.FILE_DIRECTORY_FILE, null);
          if (status == NTStatus.STATUS_SUCCESS) {
            status = tree.QueryDirectory(out List<QueryDirectoryFileInformation> files, handle, "*", FileInformationClass.FileDirectoryInformation);
            foreach (QueryDirectoryFileInformation file in files) {
              if (file.FileInformationClass.Equals(FileInformationClass.FileDirectoryInformation)) {
                FileDirectoryInformation fileDirectoryInformation = (FileDirectoryInformation)file;
                if (fileDirectoryInformation.FileName.Equals(".") || fileDirectoryInformation.FileName.Equals("..")) {
                  continue;
                }
                if (fileDirectoryInformation.FileAttributes.HasFlag(SMBLibrary.FileAttributes.Directory)) {
                  responseSb.AppendLine("directory\t" + fileDirectoryInformation.FileName);
                } else {
                  responseSb.Append("file\t" + fileDirectoryInformation.FileName);
                  responseSb.Append("\t" + fileDirectoryInformation.LastAccessTime.ToString(Format));
                  responseSb.Append("\t" + fileDirectoryInformation.LastWriteTime.ToString(Format));
                  responseSb.AppendLine("\t" + fileDirectoryInformation.CreationTime.ToString(Format));
                }
              }
            }
          } else {
            status = tree.CreateFile(out handle, out fs, path == null ? "" : path, AccessMask.GENERIC_READ, 0, ShareAccess.Read, CreateDisposition.FILE_OPEN,
                         CreateOptions.FILE_NON_DIRECTORY_FILE, null);
            if (status == NTStatus.STATUS_SUCCESS) {
              responseSb.AppendLine("file");
              status = tree.GetFileInformation(out FileInformation fileInformation, handle, FileInformationClass.FileBasicInformation);
              if (status == NTStatus.STATUS_SUCCESS) {
                FileBasicInformation fileAllInformation = (FileBasicInformation)fileInformation;
                status = tree.GetSecurityInformation(out SecurityDescriptor securityDescriptor, handle, SecurityInformation.GROUP_SECURITY_INFORMATION);
                if (status == NTStatus.STATUS_SUCCESS) {
                  StringBuilder stringBuilder = new StringBuilder();
                  responseSb.AppendLine(stringBuilder.ToString());
                  responseSb.AppendLine(fileAllInformation.CreationTime.Time.Value.ToString(Format));
                  responseSb.AppendLine(fileAllInformation.LastAccessTime.Time.Value.ToString(Format));
                  responseSb.AppendLine(fileAllInformation.LastWriteTime.Time.Value.ToString(Format));
                  responseSb.AppendLine(fileAllInformation.Length + "");                  
                } else {
                  responseSb.Append("Error: Did not get success status trying to get file security information = ");
                  responseSb.AppendLine(status.ToString());
                }
              } else {
                responseSb.Append("Error: Did not get success status trying to connect to file all information = ");
                responseSb.AppendLine(status.ToString());
              }
            } else {
              responseSb.Append("Error: Did not get success status trying to connect to file = ");
              responseSb.AppendLine(status.ToString());
            }
          }
        } else {
          responseSb.Append("Error: Did not get success status trying to connect to share tree = ");
          responseSb.AppendLine(status.ToString());
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

    public void Dispose() {
      smb.Disconnect();
    }
  }
}