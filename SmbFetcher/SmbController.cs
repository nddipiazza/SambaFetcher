using Unosquare.Labs.EmbedIO;
using Unosquare.Labs.EmbedIO.Constants;
using Unosquare.Labs.EmbedIO.Modules;

namespace SmbFetcher {
  public class SmbController : WebApiController {
    public SmbController(IHttpContext context) : base(context) {
    }

    [WebApiHandler(HttpVerbs.Get, "/api/{url}")]
    public bool GetFile(string url) {
      return this.JsonResponse("\"hi\"");
    }

    public override void SetDefaultHeaders() => this.NoCache();
  }
}
