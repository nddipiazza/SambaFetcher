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
using System.Security.Principal;
using System.Net;

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

    private string GetSidString(SID sid) {
      byte[] bytes = new byte[sid.Length];
      int offset = 0;
      sid.WriteBytes(bytes, ref offset);
      SecurityIdentifier securityIdentifier = new SecurityIdentifier(bytes, 0);
      return securityIdentifier.Value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SmbServerModule"/> class.
    /// </summary>
    /// <param name="basePath">The base path.</param>
    /// <param name="jsonPath">The json path.</param>
    public SmbServerModule(string host, string domain, string username, string password, string share) {
      AddHandler(Unosquare.Labs.EmbedIO.ModuleMap.AnyPath, HttpVerbs.Head, (context, ct) => HandleGet(context, ct, false));
      AddHandler(Unosquare.Labs.EmbedIO.ModuleMap.AnyPath, HttpVerbs.Get, (context, ct) => HandleGet(context, ct));
      smb = new SMB2Client();
      if (password == null || password.Trim().Length == 0) {
        password = Environment.GetEnvironmentVariable("PWD");
      }
      if (!smb.Connect(Dns.GetHostEntry(host).AddressList[0], SMBTransportType.DirectTCPTransport)) {
        Console.WriteLine("Error connecting.");
      }
      var status = smb.Login(domain == null ? string.Empty : domain, username, password);
      if (status != NTStatus.STATUS_SUCCESS) {
        throw new Exception("Could not connect to smb server. Status: " + status);
      } else {
        NTStatus connectStatus;
        SMB2FileStore tree = smb.TreeConnect(share, out connectStatus) as SMB2FileStore;
        if (connectStatus != NTStatus.STATUS_SUCCESS) {
          throw new Exception("Connecting to tree resulted in " + connectStatus);
        }
        tree.Disconnect();
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
      Console.WriteLine("Request action={0}, path={1}", action, path);

      if (action == null) {
        Console.WriteLine("No action specified. Returning 200 status");
        return true;
      }

      if ("download".Equals(action)) {
        NTStatus status;
        SMB2FileStore tree = smb.TreeConnect(share, out status) as SMB2FileStore;
        if (status == NTStatus.STATUS_SUCCESS) {
          object handle;
          FileStatus fs;
          status = tree.CreateFile(out handle, out fs, path == null ? "" : path, AccessMask.GENERIC_READ, 0, ShareAccess.Read, CreateDisposition.FILE_OPEN,
                         CreateOptions.FILE_NON_DIRECTORY_FILE, null);
          if (status == NTStatus.STATUS_SUCCESS) {
            try {
              int bytesCount = 0;
              byte[] data;
              do {
                status = tree.ReadFile(out data, handle, bytesCount, ChunkSize);
                if (status == NTStatus.STATUS_SUCCESS) {
                  WriteToOutputStream(context.Response, data, ct);
                  bytesCount += data.Length;
                } else {
                  throw new Exception("Couldn't get all the file data");
                }
              } while (data.Length != ChunkSize && data.Length > 0);
            } finally {
              tree.CloseFile(handle);
            }
          }
        }
        context.Response.ContentType = "application/octet-stream";
        context.Response.AddHeader(Headers.CacheControl, "no-cache");
        context.Response.AddHeader("Pragma", "no-cache");
        context.Response.AddHeader("Expires", "0");
        await context.Response.OutputStream.FlushAsync();
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
            responseSb.AppendLine("directory"); // It is a directory
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
              responseSb.AppendLine("file"); // It is not a directory
              status = tree.GetFileInformation(out FileInformation fileInformation, handle, FileInformationClass.FileBasicInformation);
              if (status == NTStatus.STATUS_SUCCESS) {
                FileBasicInformation fileAllInformation = (FileBasicInformation)fileInformation;
                status = tree.GetSecurityInformation(out SecurityDescriptor securityDescriptor,
                   handle,
                   SecurityInformation.OWNER_SECURITY_INFORMATION |
                    SecurityInformation.GROUP_SECURITY_INFORMATION |
                    SecurityInformation.DACL_SECURITY_INFORMATION);
                if (status == NTStatus.STATUS_SUCCESS) {
                  int aceIdx = 0;
                  foreach (ACE ace in securityDescriptor.Dacl) {
                    if (ace is AceType1) {
                      AceType1 aceType = (AceType1)ace;
                      if (aceType.Header.AceType == AceType.ACCESS_ALLOWED_ACE_TYPE) {
                        responseSb.Append("WINA");
                      } else {
                        responseSb.Append("WIND");
                      }
                      responseSb.Append(GetSidString(aceType.Sid));
                    }
                    if (aceIdx++ != 0) {
                      responseSb.Append(",");
                    }
                  }
                  responseSb.AppendLine();
                  responseSb.AppendLine(fileAllInformation.CreationTime.Time.Value.ToString(Format));
                  responseSb.AppendLine(fileAllInformation.LastAccessTime.Time.Value.ToString(Format));
                  responseSb.AppendLine(fileAllInformation.LastWriteTime.Time.Value.ToString(Format));
                  responseSb.AppendLine(fileAllInformation.Length + "");
                  responseSb.AppendLine(GetSidString(securityDescriptor.GroupSid));
                  responseSb.AppendLine(GetSidString(securityDescriptor.OwnerSid));
                } else {
                  Console.WriteLine("Error: Did not get success status trying to get file metadata = {0}", status);
                  context.Response.StatusCode = 500;
                  return true;
                }
              } else {
                Console.WriteLine("Error: Did not get success status trying to connect to file all information = {0}", status);
                context.Response.StatusCode = 500;
                return true;
              }
            } else {
              Console.WriteLine("Error: Did not get success status trying to connect to file = {0}", status);
              context.Response.StatusCode = 500;
              return true;
            }
          }
        } else {
          Console.WriteLine("Error: Did not get success status trying to connect to share tree = {0}", status);
          context.Response.StatusCode = 500;
          return true;
        }
        byte[] resBytes = utf8.GetBytes(responseSb.ToString());
        context.Response.ContentType = "text/plain";
        context.Response.OutputStream.Write(resBytes, 0, resBytes.Length);
      }
      return true;
    }

    static void WriteToOutputStream(
      Unosquare.Labs.EmbedIO.IHttpResponse response,
        byte[] buffer,
        CancellationToken ct) {
      response.OutputStream.Write(buffer, 0, buffer.Length);
    }

    public void Dispose() {
      smb.Disconnect();
    }
  }
}