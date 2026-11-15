using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Infinigame
{
    // Words from a language model: Claude or ChatGPT, whichever the player chooses, reached by the
    // first of three roads that is open.
    //
    // A key of the player's own, from the environment or handed over from the clipboard: the game
    // asks the maker's API itself. This road is open on every device, a phone included.
    //
    // For ChatGPT, OpenAI's Sign in with ChatGPT: the player approves in a browser and what the
    // game asks counts against their ChatGPT plan. Anthropic allows no sign-in to Claude inside
    // other people's apps, so Claude has no such road.
    //
    // On a desk, each maker's own command-line clients with the sign-ins they keep for themselves:
    // Anthropic's ant, paid from the API credits that Claude Max, Ultra and Team plans include
    // each month, or failing that Claude Code, counted against the player's Claude plan; and
    // OpenAI's codex, counted against the Codex use of any ChatGPT plan. The game can start
    // each one's own sign-in, which happens in the player's browser and never in the game.
    //
    // No key is ever built into the game. What the player gives it stays in one file on their device.
    public static class Oracle
    {
        // Given who the model should be and what to answer, returns its reply. Tests swap this for
        // a scripted reply so they never ask anyone.
        public static Func<string, string, Task<string>> Ask = Run;

        public static readonly string[] Voices = { "Claude", "ChatGPT" };
        // Each maker's models, quickest and cheapest first: an entry is a few sentences, and the
        // player is waiting. The prices are dollars for a million tokens in and out, where published.
        public static readonly (string id, string name, double input, double output)[][] Models =
        {
            new[] { ("claude-haiku-5-5", "Haiku 5.5", 0.1, 0.5), ("claude-sonnet-5-5", "Sonnet 5.5", 2.0, 10.0), ("claude-opus-5-5", "Opus 5.5", 4.0, 20.0) },
            new[] { ("gpt-6-luna", "GPT-6 Luna", 0.0, 0.0), ("gpt-6.1-terra", "GPT-6.1 Terra", 0.0, 0.0), ("gpt-6.1-sol", "GPT-6.1 Sol", 0.0, 0.0), ("gpt-6-astra", "GPT-6 Astra", 0.0, 0.0) },
        };

        // How hard a model is asked to think before it answers: the less, the sooner.
        public static readonly string[] Efforts = { "low", "medium", "high" };

        // Tests point these at stand-ins: the commands to run (ant, codex and Claude Code), the
        // servers to ask (Anthropic's API, OpenAI's API and OpenAI's sign-in), how a page is opened
        // and the file things are kept in.
        public static readonly string[] Tools = { "ant", "codex", "claude" };
        static readonly string[] ToolNames = { "ANT", "CODEX", "CLAUDE CODE" };
        // Which tools reach each voice, in the order they are tried.
        static readonly int[][] Reach = { new[] { 0, 2 }, new[] { 1 } };
        public static readonly string[] Hosts = { "https://api.anthropic.com", "https://api.openai.com", "https://auth.openai.com" };
        public static string Store = "accounts.json";
#if UNITY_IOS && !UNITY_EDITOR
        // on a phone the page opens over the game, which has to keep running to hear the browser come back
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void InfinigameBrowse(string url);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void InfinigameBrowseDone();
        public static Action<string> Browse = InfinigameBrowse;
#else
        public static Action<string> Browse = Application.OpenURL;
#endif

        static readonly string[] Makers = { "Anthropic", "OpenAI" }, Keys = { "ANTHROPIC_API_KEY", "OPENAI_API_KEY" };
        const string Resource = "https://api.openai.com/v1";
        const int Patience = 90000, SignInPatience = 330000;

        // What the player has chosen and given, and what has been asked for with it.
        [Serializable]
        class Account
        {
            public int voice;
            public int[] model = new int[2], effort = new int[2];
            public string[] key = { "", "" };
            // the ChatGPT sign-in: this device's name for itself, the client OpenAI made of it, and its passes
            public string host = "", client = "", refresh = "", access = "";
            public long expires;
            public int[] asked = new int[2];
            public long[] input = new long[2], output = new long[2];
            public double[] cost = new double[2];
        }

        // One answer, however it was come by.
        struct Answer
        {
            public string text, error;
            // refused: not signed in. included: paid for by a plan, whatever the tokens would have cost.
            public bool refused, included;
            public int input, output;
        }

        // The parts of the makers' replies that are read. Every field starts out empty so that one
        // a reply leaves out is safe to look at.
        [Serializable]
        class Fault
        {
            public string code = "", message = "";
        }

        [Serializable]
        class Tally
        {
            public int input_tokens, output_tokens, cache_creation_input_tokens, cache_read_input_tokens;
        }

        [Serializable]
        class Block
        {
            public string type = "", text = "";
        }

        // Anthropic's Messages API, and ant's one-line digest of it.
        [Serializable]
        class Message
        {
            public Block[] content = new Block[0];
            public Tally usage = new Tally();
            public Fault error = new Fault();
            public string text = "";
            public int input, output;
        }

        // One event of OpenAI's Responses API as it streams, or of codex as it works.
        [Serializable]
        class Step
        {
            public string type = "", delta = "", message = "";
            public Block item = new Block();
            public Tally usage = new Tally();
            public Fault error = new Fault();
            public Outcome response = new Outcome();
        }

        [Serializable]
        class Outcome
        {
            public Tally usage = new Tally();
            public Fault error = new Fault();
        }

        // What Claude Code has to say for itself when it is done.
        [Serializable]
        class Verdict
        {
            public string result = "";
            public bool is_error;
            public Tally usage = new Tally();
        }

        [Serializable]
        class Grant
        {
            public string access_token = "", refresh_token = "";
            public long expires_in;
        }

        static Account held = new Account();
        static string path;
        static Task renewing;

        // Reads what the player has given the game on this device. Called from the main thread as the game starts.
        public static void Load()
        {
            path = Path.Combine(Application.persistentDataPath, Store);
            held = new Account();
#if UNITY_IOS && !UNITY_EDITOR
            IntPtr value = InfinigameReadAccount(Store);
            if (value != IntPtr.Zero)
            {
                JsonUtility.FromJsonOverwrite(System.Runtime.InteropServices.Marshal.PtrToStringUTF8(value), held);
                InfinigameFreeAccount(value);
            }
#else
            if (File.Exists(path)) JsonUtility.FromJsonOverwrite(File.ReadAllText(path), held);
#endif
        }

        static void Keep()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (InfinigameWriteAccount(Store, JsonUtility.ToJson(held)) != 0) throw new IOException("The device could not save the account in its Keychain.");
#else
            if (path != null) File.WriteAllText(path, JsonUtility.ToJson(held));
#endif
        }
#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern IntPtr InfinigameReadAccount(string name);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void InfinigameFreeAccount(IntPtr value);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int InfinigameWriteAccount(string name, string value);
#endif

        // Which voice writes, with which of its models and how much thought.
        public static int Voice
        {
            get => held.voice;
            set
            {
                held.voice = value;
                Keep();
            }
        }

        public static (string id, string name, double input, double output) Model(int voice) => Models[voice][held.model[voice] % Models[voice].Length];

        public static void NextModel(int voice)
        {
            held.model[voice] = (held.model[voice] + 1) % Models[voice].Length;
            Keep();
        }

        public static string Effort(int voice) => Efforts[held.effort[voice] % Efforts.Length];

        public static void NextEffort(int voice)
        {
            held.effort[voice] = (held.effort[voice] + 1) % Efforts.Length;
            Keep();
        }

        static string Key(int voice) => new[] { Environment.GetEnvironmentVariable(Keys[voice]), held.key[voice] }.FirstOrDefault(key => !string.IsNullOrEmpty(key)) ?? "";

        // Takes a key of the player's own for a voice, as copied from its maker's site, and says whether it looked like one.
        public static bool UseKey(int voice, string key)
        {
            key = (key ?? "").Trim();
            if (!key.StartsWith("sk-") || key.StartsWith("sk-ant-") != (voice == 0) || key.Length < 20 || key.Any(char.IsWhiteSpace)) return false;
            held.key[voice] = key;
            Keep();
            return true;
        }

        // Drops the key and the sign-in the game holds for a voice. A command-line client's own sign-in is its own business.
        public static void Forget(int voice)
        {
            held.key[voice] = "";
            if (voice == 1) held.refresh = held.access = "";
            Keep();
        }

        // What a voice has been asked for since the game was installed.
        public static string Used(int voice) => held.asked[voice] == 0 ? "NOT ASKED ANYTHING YET"
            : $"ASKED {held.asked[voice]:#,0} {(held.asked[voice] == 1 ? "TIME" : "TIMES")}   ·   {held.input[voice]:#,0} TOKENS IN   ·   {held.output[voice]:#,0} OUT"
                + (held.cost[voice] >= 0.005 ? $"   ·   ABOUT ${held.cost[voice]:0.00}" : "");

        public static async Task<string> Run(string persona, string prompt)
        {
            int voice = Voice;
            var model = Model(voice);
            string who = Voices[voice], key = Key(voice), effort = Effort(voice);
            Answer answer;
            if (key.Length > 0) answer = voice == 0 ? await Claude(key, model.id, effort, persona, prompt) : await ChatGpt(key, model.id, effort, persona, prompt);
            else if (voice == 1 && held.refresh.Length > 0) answer = await ChatGpt(await Pass(), model.id, effort, persona, prompt);
            else answer = await Task.Run(() => voice == 0 ? Tooled(model.id, effort, persona, prompt) : Codex(model.id, effort, persona, prompt));

            if (answer.refused)
                throw new InvalidOperationException($"Not signed in to {who}. Open Claude or ChatGPT on the menu to sign in or give a key, or to have the other write.");
            if (!string.IsNullOrEmpty(answer.error)) throw new InvalidOperationException($"{who} could not answer. {answer.error}");
            if (string.IsNullOrWhiteSpace(answer.text)) throw new InvalidOperationException($"{who} had nothing to say.");
            held.asked[voice]++;
            held.input[voice] += answer.input;
            held.output[voice] += answer.output;
            if (!answer.included) held.cost[voice] += (answer.input * model.input + answer.output * model.output) / 1e6;
            Keep();
            return answer.text.Trim();
        }

        // Whether a voice can be reached, and how.
        public static async Task<string> Status(int voice)
        {
            if (Key(voice).Length > 0) return "YOUR OWN KEY";
            if (voice == 1 && held.refresh.Length > 0) return "SIGNED IN WITH CHATGPT";
            return await Task.Run(() => Reach[voice].Where(SignedIn).Select(tool => $"SIGNED IN WITH {ToolNames[tool]}").FirstOrDefault() ?? "NOT SIGNED IN");
        }

        // Whether one of the makers' tools is here and holds a sign-in: ant lists those it has,
        // codex and Claude Code say whether they have one.
        static bool SignedIn(int tool)
        {
            try
            {
                var (code, output, _) = tool == 0 ? Start(0, Patience, "profile", "list") : tool == 1 ? Start(1, Patience, "login", "status") : Start(2, Patience, "auth", "status");
                return code == 0 && (tool == 1 || (tool == 0 ? output.Length > 0 : output.Replace(" ", "").Contains("\"loggedIn\":true")));
            }
            catch (Exception)
            {
                return false;
            }
        }

        // What the two ways of signing in to a voice are called, for the menu.
        public static string[] Roads(int voice) => voice == 0 ? new[] { "Sign in with Claude Code", "Sign in with ant" } : new[] { "Sign in with Codex", "Continue with ChatGPT" };

        // Signs the player in to their own account in their browser, by the first or the second
        // of a voice's roads. Nothing is typed into the game: Claude Code, ant and codex each run
        // their maker's own sign-in, and ChatGPT's second road is OpenAI's own page. Returns where
        // that leaves the player, as Status does.
        public static async Task<string> SignIn(int voice, int road)
        {
            try
            {
                if (voice == 1 && road == 1) await Continue();
                else await Task.Run(() => voice == 1 ? Start(1, SignInPatience, "login") : Start(road == 0 ? 2 : 0, SignInPatience, "auth", "login"));
            }
            catch (Exception)
            {
                // never approved, or no way to ask: either way Status says so
            }
            return await Status(voice);
        }

        // ------------------------------------------------------------------ asking the makers' APIs

        // Sends a request and waits for it while the game carries on. Unity's own requests work on
        // every device it runs on.
        static async Task<(long code, string body)> Send(UnityWebRequest request)
        {
            using (request)
            {
                request.downloadHandler ??= new DownloadHandlerBuffer();
                request.timeout = Patience / 1000;
                var sent = request.SendWebRequest();
                while (!sent.isDone) await Task.Yield();
                return (request.responseCode, request.responseCode == 0 ? request.error : request.downloadHandler.text ?? "");
            }
        }

        static UnityWebRequest Post(string url, string json, params string[] headers)
        {
            var request = new UnityWebRequest(url, "POST") { uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)) };
            request.SetRequestHeader("Content-Type", "application/json");
            for (int i = 0; i < headers.Length; i += 2) request.SetRequestHeader(headers[i], headers[i + 1]);
            return request;
        }

        static UnityWebRequest Form(string url, params string[] fields)
        {
            var form = new WWWForm();
            for (int i = 0; i < fields.Length; i += 2) form.AddField(fields[i], fields[i + 1]);
            return UnityWebRequest.Post(url, form);
        }

        // A reply as one of the shapes above. One that is not JSON at all reads as empty.
        static T Read<T>(string json) where T : new()
        {
            try
            {
                return JsonUtility.FromJson<T>(json) ?? new T();
            }
            catch (ArgumentException)
            {
                return new T();
            }
        }

        // Anthropic's Messages API, with the player's key.
        static async Task<Answer> Claude(string key, string model, string effort, string persona, string prompt)
        {
            var (code, body) = await Send(Post($"{Hosts[0]}/v1/messages",
                $"{{\"model\":{Json(model)},\"max_tokens\":2000,\"system\":{Json(persona)},\"output_config\":{{\"effort\":{Json(effort)}}},\"messages\":[{{\"role\":\"user\",\"content\":{Json(prompt)}}}]}}",
                "x-api-key", key, "anthropic-version", "2026-10-21"));
            var reply = Read<Message>(body);
            return new Answer
            {
                refused = code == 401, error = code == 200 ? null : reply.error.message.Length > 0 ? reply.error.message : $"{body.Split('\n')[0]} ({code})",
                text = string.Concat(reply.content.Where(block => block.type == "text").Select(block => block.text)),
                input = reply.usage.input_tokens, output = reply.usage.output_tokens,
            };
        }

        // OpenAI's Responses API, with the player's key or the pass their ChatGPT sign-in gave. It is
        // asked to stream, as a sign-in requires, and the answer is whatever the stream added up to.
        static async Task<Answer> ChatGpt(string bearer, string model, string effort, string persona, string prompt)
        {
            var (code, body) = await Send(Post($"{Hosts[1]}/v1/responses",
                $"{{\"model\":{Json(model)},\"instructions\":{Json(persona)},\"input\":[{{\"role\":\"user\",\"content\":{Json(prompt)}}}],\"reasoning\":{{\"effort\":{Json(effort)}}},\"store\":false,\"stream\":true}}",
                "Authorization", $"Bearer {bearer}"));
            var answer = new Answer { refused = code == 401 };
            var text = new StringBuilder();
            bool finished = false;
            // a stream is a line of data for each event; a refusal is one object
            foreach (string line in code == 200 ? body.Split('\n').Where(line => line.StartsWith("data:")).Select(line => line.Substring(5)) : new[] { body })
            {
                var step = Read<Step>(line);
                if (step.type == "response.output_text.delta") text.Append(step.delta);
                if (step.type == "response.completed")
                {
                    finished = true;
                    (answer.input, answer.output) = (step.response.usage.input_tokens, step.response.usage.output_tokens);
                }
                // a failure names itself in the event or the refusal it comes in
                answer.error ??= new[] { step.response.error.message, step.response.error.code, step.error.message, step.error.code }.FirstOrDefault(word => word.Length > 0);
            }
            if (code != 200) answer.error ??= $"{body.Split('\n')[0]} ({code})";
            else if (!finished) answer.error ??= "The answer broke off part way.";
            // a plan has only so much to give other apps, and says so with a code
            if (body.Contains("subscription_sharing_usage_limit_exceeded")) answer.error = "Your ChatGPT plan has no more to give for now: see chatgpt.com/settings/usage.";
            answer.text = text.ToString();
            return answer;
        }

        // ------------------------------------------------------------------ Sign in with ChatGPT

        // Opens OpenAI's sign-in page and waits for the player's browser to come back to this
        // device with a code, which is then traded for passes. The game needs no secret of its
        // own for this: OpenAI makes a client of each device the first time it asks.
        static async Task Continue()
        {
            if (held.host.Length == 0) held.host = $"urn:uuid:{Guid.NewGuid()}";
            string verifier = Secret(48), state = Secret(24);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                string back = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/auth/callback", challenge;
                using (var sha = SHA256.Create()) challenge = Url64(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
                Browse($"{Hosts[2]}/api/accounts/authorize?response_type=code"
                    + (held.client.Length > 0 ? $"&client_id={held.client}" : "&client_id=dynamic_agent_client&agent_name_hint=Infinigame")
                    + $"&ext_agent_host_id={Uri.EscapeDataString(held.host)}&redirect_uri={Uri.EscapeDataString(back)}"
                    + $"&scope={Uri.EscapeDataString("openid profile email offline_access resource.invoke chatgpt.tokens.use.direct")}&resource={Uri.EscapeDataString(Resource)}"
                    + $"&state={state}&nonce={Secret(24)}&code_challenge_method=S256&code_challenge={challenge}");
                var came = await Task.Run(() => Callback(listener));
#if UNITY_IOS && !UNITY_EDITOR
                InfinigameBrowseDone();
#endif
                // only the answer to this very request will do, and only for the client this device already is
                if (came == null || !came.TryGetValue("state", out string echoed) || echoed != state || !came.TryGetValue("code", out string code)) return;
                came.TryGetValue("client_id", out string client);
                if (held.client.Length > 0 && !string.IsNullOrEmpty(client) && client != held.client) return;
                if (!string.IsNullOrEmpty(client)) held.client = client;
                Granted(await Send(Form($"{Hosts[2]}/api/accounts/oauth/token", "grant_type", "authorization_code", "client_id", held.client,
                    "code", code, "code_verifier", verifier, "redirect_uri", back, "resource", Resource)));
            }
            finally
            {
                listener.Stop();
            }
        }

        // Waits for the browser to ask this device for the page the sign-in sends it back to,
        // thanks it, and returns what it brought.
        static Dictionary<string, string> Callback(TcpListener listener)
        {
            var until = DateTime.UtcNow.AddMilliseconds(SignInPatience);
            while (DateTime.UtcNow < until)
            {
                if (!listener.Pending())
                {
                    Thread.Sleep(50);
                    continue;
                }
                using var caller = listener.AcceptTcpClient();
                using var stream = caller.GetStream();
                var reader = new StreamReader(stream);
                // "GET /auth/callback?code=... HTTP/1.1": the first line says it all, but the browser is heard out
                string[] asked = (reader.ReadLine() ?? "").Split(' ');
                while (!string.IsNullOrEmpty(reader.ReadLine())) { }
                bool ours = asked.Length > 1 && asked[1].StartsWith("/auth/callback?");
                byte[] page = Encoding.UTF8.GetBytes(ours
                    ? "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nConnection: close\r\n\r\n<body style=\"font:20px sans-serif;margin:4em;background:#05080f;color:#e6f0ff\">That is all Infinigame needs. You can close this page and go back to the game.</body>"
                    : "HTTP/1.1 404 Not Found\r\nConnection: close\r\n\r\n");
                stream.Write(page, 0, page.Length);
                if (ours)
                    return asked[1].Substring(asked[1].IndexOf('?') + 1).Split('&').Select(pair => pair.Split(new[] { '=' }, 2)).Where(pair => pair.Length == 2)
                        .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));
            }
            return null;
        }

        // Keeps the passes a sign-in or a renewal was given. A refusal leaves the player signed out.
        static void Granted((long code, string body) reply)
        {
            var grant = Read<Grant>(reply.body);
            bool good = reply.code == 200 && grant.access_token.Length > 0;
            // each renewal's pass is good once only, and comes with the next
            held.access = good ? grant.access_token : "";
            held.refresh = good ? grant.refresh_token : "";
            held.expires = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + grant.expires_in;
            Keep();
        }

        // The pass that goes with each request made on a ChatGPT sign-in, renewed when it has run
        // out. Renewals take turns, since each uses up the last one's pass.
        static async Task<string> Pass()
        {
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > held.expires - 60)
            {
                renewing ??= Renew();
                try
                {
                    await renewing;
                }
                finally
                {
                    renewing = null;
                }
            }
            return held.access;
        }

        static async Task Renew() => Granted(await Send(Form($"{Hosts[2]}/api/accounts/oauth/token", "grant_type", "refresh_token", "client_id", held.client,
            "refresh_token", held.refresh, "resource", Resource)));

        static string Url64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // Something nobody could guess, safe to put in a web address.
        static string Secret(int bytes)
        {
            var chance = new byte[bytes];
            using (var dice = RandomNumberGenerator.Create()) dice.GetBytes(chance);
            return Url64(chance);
        }

        // ------------------------------------------------------------------ the makers' own tools

        // ant: every value is given as JSON, and the reply asked for as one line of it: the text of
        // the answer, why it stopped and what it took.
        static Answer Ant(string model, string effort, string persona, string prompt)
        {
            // ant reads a value that starts with @ as the name of a file to send, unless a backslash comes first
            string Value(string words) => Json(words.StartsWith("@") ? "\\" + words : words);
            var (code, output, errors) = Start(0, Patience, "messages", "create", "--model", model, "--max-tokens", "2000", "--system", Value(persona),
                "--output-config", $"{{\"effort\":{Json(effort)}}}", "--message", $"{{\"role\":\"user\",\"content\":{Value(prompt)}}}",
                "--transform", "{text:content.#(type==\"text\").text,stop_reason,input:usage.input_tokens,output:usage.output_tokens}", "--format", "jsonl",
                "--transform-error", "error.message", "--format-error", "yaml", "--raw-output");
            var reply = code == 0 ? Read<Message>(output) : new Message();
            // it sends a request with no sign-in anyway, and puts the API's words on one pipe and the status they came with on the other
            return new Answer
            {
                refused = code != 0 && errors.Contains("401"), error = code == 0 ? null : (output.Length > 0 ? output : errors).Split('\n')[0],
                text = reply.text, input = reply.input, output = reply.output,
            };
        }

        // Claude with no key: ant where it holds a sign-in, since it asks the API and nothing
        // more, and otherwise Claude Code with the sign-in that keeps. Where there is no Claude
        // Code either, ant has the last word on what is wrong.
        static Answer Tooled(string model, string effort, string persona, string prompt)
        {
            if (!SignedIn(0))
                try
                {
                    return ClaudeCode(model, effort, persona, prompt);
                }
                catch (InvalidOperationException)
                {
                    // no Claude Code on this computer
                }
            return Ant(model, effort, persona, prompt);
        }

        // Claude Code is a coding agent, so it is told to be nothing of the kind: the persona in
        // place of its own instructions, no tools, none of the player's settings or servers,
        // nothing kept afterwards, and its answer as one object of JSON.
        static Answer ClaudeCode(string model, string effort, string persona, string prompt)
        {
            var (code, output, errors) = Start(2, Patience, "-p", prompt, "--model", model, "--effort", effort, "--system-prompt", persona, "--tools", "",
                "--restricted", "--strict-mcp-config", "--disable-slash-commands", "--no-session-persistence", "--output-format", "json");
            var reply = Read<Verdict>(output);
            bool failed = code != 0 || reply.is_error;
            string why = !failed ? null : new[] { reply.result, errors, output, "Claude Code gave up without saying why." }.First(words => words.Length > 0).Split('\n')[0];
            return new Answer
            {
                // signed out, it says to log in
                refused = failed && new[] { "log in", "login", "401", "authenticat" }.Any(sign => why.IndexOf(sign, StringComparison.OrdinalIgnoreCase) >= 0),
                error = why, text = failed ? null : reply.result, included = true,
                input = reply.usage.input_tokens + reply.usage.cache_creation_input_tokens + reply.usage.cache_read_input_tokens, output = reply.usage.output_tokens,
            };
        }

        // The rest of what a coding agent brings along and has no use for here. Left at home, it answers in half the time.
        static readonly string[] Spare =
        {
            "unified_exec", "apps", "plugins", "browser_use", "computer_use", "image_generation", "view_image", "tool_suggest", "skill_search", "goals", "multi_agent", "sleep_tool",
            "in_app_browser",
        };

        // codex is a coding agent, so it is told to be nothing of the kind: the persona takes the
        // place of its own instructions, it gets no shell, none of the player's settings and an
        // empty folder to stand in, and it reports as a line of JSON for each thing it does.
        static Answer Codex(string model, string effort, string persona, string prompt)
        {
            string folder = Path.Combine(Path.GetTempPath(), "oracle"), brief = Path.Combine(folder, $"{(uint)persona.GetHashCode():x8}.txt");
            Directory.CreateDirectory(folder);
            File.WriteAllText(brief, persona);
            (int code, string output, string errors) Exec(string[] without) => Start(1, Patience, new[]
            {
                "exec", "--json", "--skip-git-repo-check", "--ephemeral", "--ignore-user-config", "--ignore-rules",
                "-C", folder, "-m", model, "-c", $"model_reasoning_effort={Json(effort)}", "-c", $"model_instructions_file={Json(brief)}",
            }.Concat(without.Prepend("shell_tool").SelectMany(feature => new[] { "--disable", feature })).Append(prompt).ToArray());
            var (code, output, errors) = Exec(Spare);
            // a codex that no longer knows one of those names says so and stops: ask again with only the one that matters
            if (code != 0 && errors.Contains("Unknown feature flag")) (code, output, errors) = Exec(new string[0]);
            var answer = new Answer();
            foreach (var step in output.Split('\n').Select(Read<Step>))
            {
                if (step.type == "item.completed" && step.item.type == "agent_message") answer.text = step.item.text;
                if (step.type == "turn.completed") (answer.input, answer.output) = (step.usage.input_tokens, step.usage.output_tokens);
                // the last thing to go wrong is the one that stopped it
                if (step.type == "turn.failed") answer.error = step.error.message;
                else if (step.type == "error" && code != 0) answer.error = step.message;
            }
            if (code != 0) answer.error ??= "codex gave up without saying why.";
            // it too sends a request with no sign-in, and the API turns it away
            answer.refused = code != 0 && answer.error.Contains("401");
            answer.included = true;
            return answer;
        }

        // Runs one of the makers' tools and waits for it: its exit code and what it printed.
        static (int code, string output, string errors) Start(int tool, int patience, params string[] arguments)
        {
            int voice = tool == 1 ? 1 : 0;
#if UNITY_IOS || UNITY_ANDROID
            throw new InvalidOperationException($"No way to reach {Voices[voice]} on this device without a key or a sign-in from the menu.");
#else
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            // an app started from the Finder has a bare PATH, so look where the tools get installed too
            string found = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Concat(new[] { "/opt/homebrew/bin", "/usr/local/bin", Path.Combine(home, ".local/bin"), Path.Combine(home, "go/bin"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"), Path.Combine(home, "AppData/Local/Microsoft/WinGet/Links") })
                .SelectMany(place => new[] { Path.Combine(place, Tools[tool] + ".exe"), Path.Combine(place, Tools[tool]) }).FirstOrDefault(File.Exists);
            string entry = null;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT && (found == null || Path.GetExtension(found) != ".exe"))
            {
                string package = tool == 1 ? "@openai/codex/bin/codex.js" : tool == 2 ? "@anthropic-ai/claude-code/cli.js" : null;
                if (package != null)
                {
                    entry = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm/node_modules", package);
                    if (!File.Exists(entry)) entry = null;
                }
                found = entry == null ? null : (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                    .Select(place => Path.Combine(place, "node.exe")).FirstOrDefault(File.Exists);
            }
            if (found == null)
                throw new InvalidOperationException($"No way to reach {Voices[voice]}. Give the game a key or sign in from the menu, or install {Makers[voice]}'s {Tools[tool]} (script/bootstrap does).");
            // an agent looks about the folder it is started in: it gets an empty one
            string folder = Path.Combine(Path.GetTempPath(), "oracle");
            Directory.CreateDirectory(folder);
            var start = new ProcessStartInfo
            {
                FileName = found, WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                // an app started from the Finder has no locale either, and without one a curly apostrophe arrives as three question marks
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            if (entry != null) start.ArgumentList.Add(entry);
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            // nothing is fed to it, and it must not wait for anything
            process.StandardInput.Close();
            // read both pipes as they fill so neither can block the tool
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(patience))
            {
                process.Kill();
                throw new TimeoutException($"{Voices[voice]} took too long to answer.");
            }
            return (process.ExitCode, output.Result.Trim(), errors.Result.Trim());
#endif
        }

        // A string as JSON. Backslashes go by their code: a pair of them does not survive every
        // runtime's way of handing arguments to a new process.
        static string Json(string words)
        {
            var json = new StringBuilder("\"");
            foreach (char letter in words)
                json.Append(letter == '"' ? "\\\"" : letter == '\n' ? "\\n" : letter == '\r' ? "\\r" : letter == '\t' ? "\\t"
                    : letter < ' ' || letter == '\\' ? $"\\u{(int)letter:x4}" : letter.ToString());
            return json.Append('"').ToString();
        }
    }
}
