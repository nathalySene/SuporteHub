using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SuporteHub.API.Models;
using SuporteHub.API.Services;
using SuporteHub.API.Storage;

namespace GoogleAuth.Tests;

public static class Runner
{
    const string Password = "Integration-password-only-321";
    static int passed;
    public static async Task<int> Main()
    {
        try
        {
            // Mvc.Testing resolves its generated app manifest relative to the runner cwd.
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            await using var fixture = new Fixture();
            var support = fixture.Client();
            await Register(support, "support@example.com", "Support");
            await Case("authorization-code flow uses state, correlation and PKCE", async () =>
            {
                var client = fixture.Client();
                fixture.Backchannel.User = User("subject-new", "new@example.com");
                var finish = await fixture.OAuth(client);
                Location(finish, "/#/novo");
                var me = await Me(client);
                Equal(me.GetProperty("email").GetString(), "new@example.com");
                Equal(me.GetProperty("perfil").GetString(), "client");
                True(me.GetProperty("googleLinked").GetBoolean());
                True(fixture.Backchannel.PkceValidated);
                True(!fixture.Factory.Services.GetRequiredService<IOptionsMonitor<GoogleOptions>>().Get(GoogleAuthConfiguration.Scheme).SaveTokens);
            });
            await Case("unverified and missing email or subject cannot authenticate", async () =>
            {
                foreach (var user in new[]
                {
                    User("unverified", "unverified@example.com", false),
                    new Dictionary<string, object?> { ["id"] = "no-email", ["verified_email"] = true },
                    new Dictionary<string, object?> { ["email"] = "no-subject@example.com", ["verified_email"] = true }
                })
                {
                    var client = fixture.Client(); fixture.Backchannel.User = user;
                    Location(await fixture.OAuth(client), "/#/login?erro=google-invalid");
                    Equal((await client.GetAsync("/api/auth/me")).StatusCode, HttpStatusCode.Unauthorized);
                }
            });
            var alice = fixture.Client();
            await Register(alice, "alice@example.com", "Alice");
            await Case("same email never automatically links an existing password account", async () =>
            {
                var client = fixture.Client(); fixture.Backchannel.User = User("alice-google", "alice@example.com");
                Location(await fixture.OAuth(client), "/#/login?erro=google-account-exists");
                Equal((await client.GetAsync("/api/auth/me")).StatusCode, HttpStatusCode.Unauthorized);
                True(!(await Me(alice)).GetProperty("googleLinked").GetBoolean());
            });
            await Case("explicit linking rejects a different verified Google email", async () =>
            {
                fixture.Backchannel.User = User("different", "different@example.com");
                var url = await PrepareLink(alice);
                Location(await fixture.OAuth(alice, url), "/#/conta?erro=google-email-mismatch");
                True(!(await Me(alice)).GetProperty("googleLinked").GetBoolean());
            });
            await Case("explicit linking preserves account and supports later subject login", async () =>
            {
                fixture.Backchannel.User = User("alice-google", "alice@example.com");
                var url = await PrepareLink(alice);
                Location(await fixture.OAuth(alice, url), "/#/conta?google=linked");
                True((await Me(alice)).GetProperty("googleLinked").GetBoolean());
                var client = fixture.Client();
                fixture.Backchannel.User = User("alice-google", "renamed-alice@example.com");
                Location(await fixture.OAuth(client), "/#/novo");
                Equal((await Me(client)).GetProperty("email").GetString(), "alice@example.com");
            });
            await Case("password-reset stamp invalidates an in-flight link", async () =>
            {
                var client = fixture.Client(); await Register(client, "stamp@example.com", "Stamp");
                fixture.Backchannel.User = User("stamp-google", "stamp@example.com");
                var url = await PrepareLink(client);
                var start = await fixture.Begin(client, url);
                var store = fixture.Factory.Services.GetRequiredService<IAppStore>();
                var account = (await store.Get<Conta>("USERS", "stamp@example.com"))!.Value;
                account.Value.SecurityStamp = Guid.NewGuid().ToString("N");
                await store.Put("USERS", account.Value.Email, account.Value, account.Version);
                Location(await fixture.Complete(client, start), "/#/conta?erro=google-invalid");
                True(string.IsNullOrEmpty((await store.Get<Conta>("USERS", "stamp@example.com"))!.Value.Value.GoogleSubject));
            });
            await Case("callback state and correlation are required", async () =>
            {
                var client = fixture.Client();
                var start = await fixture.Begin(client, "/api/auth/google/start");
                var stranger = fixture.Client();
                var noCorrelation = await stranger.GetAsync("/signin-google?code=fake-code&state=" + Uri.EscapeDataString(start.State));
                KnownFailure(noCorrelation);
                Equal((await stranger.GetAsync("/api/auth/me")).StatusCode, HttpStatusCode.Unauthorized);
                KnownFailure(await client.GetAsync("/signin-google?code=fake-code&state=invalid-state"));
            });
            await Case("Google cancellation does not establish an application session", async () =>
            {
                var client = fixture.Client(); var start = await fixture.Begin(client, "/api/auth/google/start");
                var response = await client.GetAsync("/signin-google?error=access_denied&state=" + Uri.EscapeDataString(start.State));
                KnownFailure(response);
                Equal((await client.GetAsync("/api/auth/me")).StatusCode, HttpStatusCode.Unauthorized);
            });
            await Case("Google signup cannot consume empty local support setup", async () =>
            {
                await using var empty = new Fixture();
                empty.Backchannel.User = User("initial-google", "initial@example.com");
                var client = empty.Client();
                Location(await empty.OAuth(client), "/#/login?erro=google-setup-required");
                var setup = await client.GetFromJsonAsync<JsonElement>("/api/auth/setup");
                True(setup.GetProperty("primeiroAcesso").GetBoolean());
                Equal((await client.GetAsync("/api/auth/me")).StatusCode, HttpStatusCode.Unauthorized);
            });
            Console.WriteLine($"PASS: {passed} Google OAuth integration checks (local test double; no real Google credentials or network).");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL after {passed} Google OAuth checks: {error.Message}");
            Console.Error.WriteLine(error.StackTrace);
            return 1;
        }
    }

    static async Task Case(string name, Func<Task> test)
    {
        await test(); passed++; Console.WriteLine("PASS: " + name);
    }
    static Dictionary<string, object?> User(string subject, string email, bool verified = true) => new()
    { ["sub"] = subject, ["email"] = email, ["email_verified"] = verified, ["name"] = "Google Test User" };
    static async Task Register(HttpClient client, string email, string name)
    {
        using var response = await Post(client, "/api/auth/cadastro", new { nome = name, email, senha = Password });
        Equal(response.StatusCode, HttpStatusCode.Created);
    }
    static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Requested-With", "SuporteHub");
        return await client.SendAsync(request);
    }
    static async Task<string> PrepareLink(HttpClient client)
    {
        using var response = await Post(client, "/api/auth/google/link", new { });
        Equal(response.StatusCode, HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var url = result.GetProperty("url").GetString()!;
        Equal(url, "/api/auth/google/start?link=1");
        return url;
    }
    static async Task<JsonElement> Me(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/me");
        Equal(response.StatusCode, HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    static void Location(HttpResponseMessage response, string expected)
    {
        Equal(response.StatusCode, HttpStatusCode.Redirect);
        Equal(response.Headers.Location?.OriginalString, expected);
    }
    static void KnownFailure(HttpResponseMessage response)
    {
        Equal(response.StatusCode, HttpStatusCode.Redirect);
        var target = response.Headers.Location?.OriginalString ?? "";
        True(target is "/#/login?erro=google-cancelled" or "/#/login?erro=google-invalid");
    }
    static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
    static void Equal<T>(T actual, T expected)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected)) throw new Exception($"Expected {expected}; got {actual}.");
    }

    sealed record OAuthStart(string State);
    sealed class Fixture : IAsyncDisposable
    {
        readonly string directory = Path.Combine(Path.GetTempPath(), "suportehub-google-" + Guid.NewGuid().ToString("N"));
        public FakeGoogle Backchannel { get; } = new();
        public GoogleFactory Factory { get; }
        public Fixture()
        {
            Directory.CreateDirectory(directory);
            Factory = new GoogleFactory(directory, Backchannel);
        }
        public HttpClient Client() => Factory.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        public async Task<OAuthStart> Begin(HttpClient client, string url)
        {
            using var response = await client.GetAsync(url);
            Equal(response.StatusCode, HttpStatusCode.Redirect);
            var location = response.Headers.Location!;
            Equal(location.Host, "accounts.google.com");
            var query = QueryHelpers.ParseQuery(location.Query);
            True(!string.IsNullOrWhiteSpace(query["state"]));
            Equal(query["redirect_uri"].ToString(), "https://localhost/signin-google");
            Equal(query["code_challenge_method"].ToString(), "S256");
            True(query["scope"].ToString().Contains("email"));
            Backchannel.Challenge = query["code_challenge"].ToString();
            return new(query["state"].ToString());
        }
        public async Task<HttpResponseMessage> Complete(HttpClient client, OAuthStart start)
        {
            using var callback = await client.GetAsync("/signin-google?code=fake-code&state=" + Uri.EscapeDataString(start.State));
            Equal(callback.StatusCode, HttpStatusCode.Redirect);
            if (callback.Headers.Location?.OriginalString != "/api/auth/google/finish") throw new Exception("OAuth callback failed: " + Backchannel.RemoteFailure);
            return await client.GetAsync(callback.Headers.Location);
        }
        public async Task<HttpResponseMessage> OAuth(HttpClient client, string url = "/api/auth/google/start") => await Complete(client, await Begin(client, url));
        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    sealed class GoogleFactory(string directory, FakeGoogle backchannel) : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Authentication:Google:ClientId", "google-integration-client");
            builder.UseSetting("Authentication:Google:ClientSecret", "google-integration-secret");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Google:ClientId"] = "google-integration-client",
                ["Authentication:Google:ClientSecret"] = "google-integration-secret",
                ["Storage:Provider"] = "Sqlite", ["Storage:DatabasePath"] = Path.Combine(directory, "application.sqlite3"),
                ["Storage:KeysPath"] = Path.Combine(directory, "keys"), ["Storage:UploadsPath"] = Path.Combine(directory, "uploads")
            }));
            builder.ConfigureTestServices(services => services.PostConfigure<GoogleOptions>(GoogleAuthConfiguration.Scheme, options =>
            {
                options.Backchannel = new HttpClient(backchannel, disposeHandler: false);
                var remoteFailure = options.Events.OnRemoteFailure;
                options.Events.OnRemoteFailure = context =>
                {
                    backchannel.RemoteFailure = context.Failure?.ToString() ?? "unknown failure";
                    return remoteFailure(context);
                };
            }));
        }
    }

    sealed class FakeGoogle : HttpMessageHandler
    {
        public Dictionary<string, object?> User { get; set; } = new();
        public string Challenge { get; set; } = "";
        public bool PkceValidated { get; private set; }
        public string RemoteFailure { get; set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "oauth2.googleapis.com" && request.RequestUri.AbsolutePath == "/token")
            {
                var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
                Equal(form["client_id"].ToString(), "google-integration-client");
                Equal(form["client_secret"].ToString(), "google-integration-secret");
                Equal(form["code"].ToString(), "fake-code");
                var verifier = form["code_verifier"].ToString();
                True(!string.IsNullOrEmpty(verifier));
                Equal(WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), Challenge);
                PkceValidated = true;
                return Json(new { access_token = "fake-google-access-token", token_type = "Bearer", expires_in = 3600 });
            }
            if (request.RequestUri.Host == "www.googleapis.com" && request.RequestUri.AbsolutePath is "/oauth2/v3/userinfo" or "/oauth2/v2/userinfo")
            {
                Equal(request.Headers.Authorization, new AuthenticationHeaderValue("Bearer", "fake-google-access-token"));
                return Json(User);
            }
            throw new Exception("Unexpected OAuth backchannel destination: " + request.RequestUri.GetLeftPart(UriPartial.Path));
        }
        static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
