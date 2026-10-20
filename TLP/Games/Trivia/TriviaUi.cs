using System;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;

namespace TLP.Games.Trivia;

public class TriviaUi
{
    private readonly Plugin plugin;
    private readonly TriviaGame game;
    private TriviaConfig Config => plugin.Config.Trivia;
    private TriviaGame Game => game;

    private static readonly Vector4 Green = new(0.45f, 1f, 0.55f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.82f, 0.3f, 1f);
    private static readonly Vector4 Grey = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Pink = new(1f, 0.4f, 0.8f, 1f);

    // question editor buffers
    private string newQuestion = string.Empty;
    private string newAnswer = string.Empty;
    private Guid? editing;
    private string editQuestion = string.Empty;
    private string editAnswer = string.Empty;
    private string bulkImport = string.Empty;
    private string filter = string.Empty;
    private readonly Random rng = new();

    public TriviaUi(Plugin plugin, TriviaGame game)
    {
        this.plugin = plugin;
        this.game = game;
    }

    public void Draw()
    {
        if (ImGui.BeginTabBar("##triviatabs"))
        {
            if (ImGui.BeginTabItem("Host")) { DrawHost(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Questions")) { DrawQuestions(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Scoreboard")) { DrawScores(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Settings")) { DrawSettings(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
    }

    // =====================================================================
    private void DrawHost()
    {
        var q = Game.Current;
        if (q == null)
        {
            var unused = Config.Questions.Where(x => !x.Used).ToList();
            ImGui.Text($"No question running.  {unused.Count} of {Config.Questions.Count} questions left tonight.");
            ImGui.Spacing();

            ImGui.BeginDisabled(unused.Count == 0);
            if (ImGui.Button("Ask next question")) Game.Ask(unused[0]);
            ImGui.SameLine();
            if (ImGui.Button("Ask random question")) Game.Ask(unused[rng.Next(unused.Count)]);
            ImGui.EndDisabled();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(Grey, "Or pick one:");
            if (ImGui.BeginTable("##pick", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY, new Vector2(0, 280)))
            {
                ImGui.TableSetupColumn("Question", ImGuiTableColumnFlags.WidthStretch, 0);
                ImGui.TableSetupColumn("Answer", ImGuiTableColumnFlags.WidthFixed, 160);
                ImGui.TableSetupColumn("##ask", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableHeadersRow();
                foreach (var item in unused)
                {
                    ImGui.PushID(item.Id.ToString());
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextWrapped(item.Question);
                    ImGui.TableNextColumn(); ImGui.TextColored(Grey, item.Answer);
                    ImGui.TableNextColumn(); if (ImGui.Button("Ask")) Game.Ask(item);
                    ImGui.PopID();
                }
                ImGui.EndTable();
            }
            DrawLastSent();
            return;
        }

        // ---- a question is running ----
        ImGui.TextColored(Pink, $"Question #{Game.Number}");
        ImGui.TextWrapped(q.Question);
        ImGui.TextColored(Gold, $"Answer: {q.Answer}");
        var secs = (int)(DateTime.Now - Game.OpenedAt).TotalSeconds;
        if (Game.CountingDown) ImGui.TextColored(Gold, $"Counting down... {Game.CountdownShowing}");
        else ImGui.TextColored(Game.Open ? Green : Grey, Game.Open ? $"Taking answers... ({secs}s)" : "Answers closed");

        if (Game.Open) { if (ImGui.Button("Close answers")) Game.SetOpen(false); }
        else { if (ImGui.Button("Reopen answers")) Game.SetOpen(true); }
        ImGui.SameLine();
        if (ImGui.Button("Re-shout question")) Game.ReshoutQuestion();

        ImGui.Separator();
        ImGui.Text($"Answers ({Game.Answers.Count})");
        ImGui.SameLine();
        ImGui.TextColored(Grey, "- tick everyone who got it right. Green = looks like a match.");

        if (ImGui.BeginTable("##answers", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new Vector2(0, 260)))
        {
            ImGui.TableSetupColumn("Right?", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthFixed, 170);
            ImGui.TableSetupColumn("Answer", ImGuiTableColumnFlags.WidthStretch, 0);
            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableSetupColumn("From", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableHeadersRow();
            foreach (var a in Game.Answers.OrderBy(a => a.Time))
            {
                ImGui.PushID(a.Key);
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.Checkbox("##sel", ref a.Selected);
                ImGui.TableNextColumn(); ImGui.Text(a.Name);
                if (!string.IsNullOrEmpty(a.World)) { ImGui.SameLine(); ImGui.TextColored(Grey, a.World); }
                ImGui.TableNextColumn();
                if (a.LooksRight) ImGui.TextColored(Green, a.Text); else ImGui.TextWrapped(a.Text);
                if (a.Attempts > 1) { ImGui.SameLine(); ImGui.TextColored(Grey, $"(+{a.Attempts - 1} more)"); }
                ImGui.TableNextColumn(); ImGui.Text($"{(int)(a.Time - Game.OpenedAt).TotalSeconds}s");
                ImGui.TableNextColumn(); ImGui.TextColored(Grey, a.Channel);
                ImGui.PopID();
            }
            ImGui.EndTable();
        }

        if (ImGui.Button("Tick all green")) foreach (var a in Game.Answers) a.Selected = a.LooksRight;
        ImGui.SameLine();
        if (ImGui.Button("Untick all")) foreach (var a in Game.Answers) a.Selected = false;

        ImGui.Spacing();
        var chosen = Game.Answers.Count(a => a.Selected);
        ImGui.BeginDisabled(chosen == 0);
        if (ImGui.Button($"Award {chosen} winner(s) + shout")) Game.AwardSelected();
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Nobody got it + shout answer")) Game.NobodyGotIt();
        ImGui.SameLine();
        if (ImGui.Button("Skip (no shout)")) Game.SkipQuietly();

        if (chosen > 0)
        {
            var names = Game.Answers.Where(a => a.Selected).Select(a => a.Name).ToList();
            ImGui.TextColored(Grey, "Will shout: " + Chat.Clean(Game.Fill(Config.CorrectTemplate, names: string.Join(", ", names))));
        }
        DrawLastSent();
    }

    private void DrawLastSent()
    {
        if (string.IsNullOrEmpty(Game.LastSent)) return;
        ImGui.Spacing();
        ImGui.TextColored(Grey, "Last message sent: " + Game.LastSent);
    }

    // =====================================================================
    private void DrawQuestions()
    {
        ImGui.Text("Add a question");
        ImGui.InputTextMultiline("Question##new", ref newQuestion, 400, new Vector2(-90, 50));
        ImGui.SetNextItemWidth(-90);
        ImGui.InputText("Answer##new", ref newAnswer, 200);
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(newQuestion) || string.IsNullOrWhiteSpace(newAnswer));
        if (ImGui.Button("Add question"))
        {
            Config.Questions.Add(new TriviaQuestion { Question = newQuestion.Trim(), Answer = newAnswer.Trim() });
            newQuestion = newAnswer = string.Empty;
            plugin.Save();
        }
        ImGui.EndDisabled();

        ImGui.Separator();
        var used = Config.Questions.Count(q => q.Used);
        ImGui.Text($"Question bank: {Config.Questions.Count}  (used tonight: {used})");
        ImGui.SameLine();
        if (ImGui.Button("Mark all unused")) { foreach (var qq in Config.Questions) qq.Used = false; plugin.Save(); }
        ImGui.SameLine();
        if (ImGui.Button("Copy all to clipboard"))
            ImGui.SetClipboardText(string.Join("\n", Config.Questions.Select(qq => $"{qq.Question} | {qq.Answer}")));
        ImGui.SetNextItemWidth(220);
        ImGui.InputText("Search##qfilter", ref filter, 100);

        if (ImGui.BeginTable("##bank", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new Vector2(0, 240)))
        {
            ImGui.TableSetupColumn("Question", ImGuiTableColumnFlags.WidthStretch, 0);
            ImGui.TableSetupColumn("Answer", ImGuiTableColumnFlags.WidthFixed, 160);
            ImGui.TableSetupColumn("Used", ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn("##edit", ImGuiTableColumnFlags.WidthFixed, 110);
            ImGui.TableHeadersRow();
            TriviaQuestion? toDelete = null;
            foreach (var q in Config.Questions)
            {
                if (filter.Length > 0 && !(q.Question.Contains(filter, StringComparison.OrdinalIgnoreCase) || q.Answer.Contains(filter, StringComparison.OrdinalIgnoreCase))) continue;
                ImGui.PushID(q.Id.ToString());
                ImGui.TableNextRow();
                if (editing == q.Id)
                {
                    ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); ImGui.InputText("##eq", ref editQuestion, 400);
                    ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); ImGui.InputText("##ea", ref editAnswer, 200);
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    if (ImGui.Button("Save")) { q.Question = editQuestion.Trim(); q.Answer = editAnswer.Trim(); editing = null; plugin.Save(); }
                    ImGui.SameLine();
                    if (ImGui.Button("Cancel")) editing = null;
                }
                else
                {
                    ImGui.TableNextColumn(); ImGui.TextWrapped(q.Question);
                    ImGui.TableNextColumn(); ImGui.TextColored(Gold, q.Answer);
                    ImGui.TableNextColumn(); var u = q.Used; if (ImGui.Checkbox("##used", ref u)) { q.Used = u; plugin.Save(); }
                    ImGui.TableNextColumn();
                    if (ImGui.Button("Edit")) { editing = q.Id; editQuestion = q.Question; editAnswer = q.Answer; }
                    ImGui.SameLine();
                    if (ImGui.Button("Del") && ImGui.GetIO().KeyCtrl) toDelete = q;
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hold Ctrl and click to delete");
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
            if (toDelete != null) { Config.Questions.Remove(toDelete); plugin.Save(); }
        }

        ImGui.Separator();
        ImGui.Text("Bulk import - one per line:  Question | Answer");
        ImGui.InputTextMultiline("##bulk", ref bulkImport, 20000, new Vector2(-1, 80));
        if (ImGui.Button("Import lines"))
        {
            var added = 0;
            foreach (var line in bulkImport.Split('\n'))
            {
                var parts = line.Split('|');
                if (parts.Length < 2) continue;
                var qq = parts[0].Trim(); var aa = string.Join("|", parts.Skip(1)).Trim();
                if (qq.Length == 0 || aa.Length == 0) continue;
                Config.Questions.Add(new TriviaQuestion { Question = qq, Answer = aa }); added++;
            }
            if (added > 0) { bulkImport = string.Empty; plugin.Save(); }
        }
    }

    // =====================================================================
    private void DrawScores()
    {
        var ranked = Game.Ranked();
        var owed = Config.Scores.Values.Sum(s => s.Unpaid);
        ImGui.Text($"Players: {ranked.Count}    Gil still to pay out: {owed:N0}");

        if (ImGui.BeginTable("##scores", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new Vector2(0, 260)))
        {
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 25);
            ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthStretch, 0);
            ImGui.TableSetupColumn("Correct", ImGuiTableColumnFlags.WidthFixed, 95);
            ImGui.TableSetupColumn("Won", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("Unpaid", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("##pay", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableHeadersRow();
            var i = 0;
            foreach (var (key, s) in ranked)
            {
                i++;
                ImGui.PushID(key);
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.Text(i.ToString());
                ImGui.TableNextColumn(); ImGui.Text(s.Display);
                ImGui.TableNextColumn();
                if (ImGui.SmallButton("-")) { s.Points = Math.Max(0, s.Points - 1); s.GilOwed = Math.Max(0, s.GilOwed - Config.GilPerAnswer); plugin.Save(); }
                ImGui.SameLine(); ImGui.Text(s.Points.ToString()); ImGui.SameLine();
                if (ImGui.SmallButton("+")) { s.Points++; s.GilOwed += Config.GilPerAnswer; plugin.Save(); }
                ImGui.TableNextColumn(); ImGui.Text(s.GilOwed.ToString("N0"));
                ImGui.TableNextColumn();
                if (s.Unpaid > 0) ImGui.TextColored(Gold, s.Unpaid.ToString("N0")); else ImGui.TextColored(Green, "paid");
                ImGui.TableNextColumn();
                ImGui.BeginDisabled(s.Unpaid == 0);
                if (ImGui.SmallButton("Mark paid")) { s.GilPaid = s.GilOwed; plugin.Save(); }
                ImGui.EndDisabled();
                ImGui.PopID();
            }
            ImGui.EndTable();
        }

        if (ImGui.Button("Shout standings (top 5)")) Game.ShoutStandings();

        ImGui.Separator();
        ImGui.TextColored(Pink, $"End of night - grand prize {Config.GrandPrize:N0} gil");
        var leaders = Game.Leaders();
        if (leaders.Count == 0) ImGui.TextColored(Grey, "No points yet.");
        else
        {
            if (leaders.Count > 1) ImGui.TextColored(Gold, $"Tie at {leaders[0].Value.Points} correct between {leaders.Count} players - pick who wins (or run a tie-breaker question first).");
            foreach (var (key, s) in leaders)
            {
                ImGui.PushID("win" + key);
                if (ImGui.Button($"Announce {s.Name} as the winner")) Game.DeclareWinner(key);
                ImGui.PopID();
            }
        }

        ImGui.Spacing();
        if (ImGui.Button("Start a new night (clear scores)") && ImGui.GetIO().KeyCtrl) Game.ResetNight();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hold Ctrl and click. Clears points and gil tracking and marks all questions unused.");
    }

    // =====================================================================
    private void DrawSettings()
    {
        var changed = false;
        ImGui.Text("Prizes");
        var gil = Config.GilPerAnswer;
        ImGui.SetNextItemWidth(140);
        if (ImGui.InputInt("Gil per correct answer", ref gil, 10000)) { Config.GilPerAnswer = Math.Max(0, gil); changed = true; }
        var prize = Config.GrandPrize;
        ImGui.SetNextItemWidth(140);
        if (ImGui.InputInt("Grand prize for the night", ref prize, 100000)) { Config.GrandPrize = Math.Max(0, prize); changed = true; }

        ImGui.Separator();
        ImGui.Text("Collect answers from");
        changed |= Box("Say", () => Config.ListenSay, v => Config.ListenSay = v); ImGui.SameLine();
        changed |= Box("Shout", () => Config.ListenShout, v => Config.ListenShout = v); ImGui.SameLine();
        changed |= Box("Yell", () => Config.ListenYell, v => Config.ListenYell = v); ImGui.SameLine();
        changed |= Box("Tells", () => Config.ListenTell, v => Config.ListenTell = v);
        changed |= Box("Party", () => Config.ListenParty, v => Config.ListenParty = v); ImGui.SameLine();
        changed |= Box("Alliance", () => Config.ListenAlliance, v => Config.ListenAlliance = v); ImGui.SameLine();
        changed |= Box("Free Company", () => Config.ListenFreeCompany, v => Config.ListenFreeCompany = v);

        changed |= Box("Only count each player's first answer", () => Config.FirstAnswerOnly, v => Config.FirstAnswerOnly = v);
        var prefix = Config.AnswerPrefix;
        ImGui.SetNextItemWidth(80);
        if (ImGui.InputText("Answer prefix (optional, e.g. !a)", ref prefix, 16)) { Config.AnswerPrefix = prefix; changed = true; }

        ImGui.Separator();
        ImGui.Text("Messages");
        ImGui.TextColored(Grey, "Placeholders: {n} {question} {answer} {names} {gil} {prize} | winner: {name} {points} | standings: {standings}");
        changed |= Template("Question", () => Config.QuestionTemplate, v => Config.QuestionTemplate = v);
        changed |= Template("Correct", () => Config.CorrectTemplate, v => Config.CorrectTemplate = v);
        changed |= Template("Nobody", () => Config.NobodyTemplate, v => Config.NobodyTemplate = v);
        changed |= Template("Standings", () => Config.StandingsTemplate, v => Config.StandingsTemplate = v);
        changed |= Template("Winner", () => Config.WinnerTemplate, v => Config.WinnerTemplate = v);

        if (changed) plugin.Save();
    }

    private static bool Box(string label, Func<bool> get, Action<bool> set)
    {
        var v = get();
        if (!ImGui.Checkbox(label, ref v)) return false;
        set(v); return true;
    }

    private static bool Template(string label, Func<string> get, Action<string> set)
    {
        var v = get();
        ImGui.SetNextItemWidth(-90);
        if (!ImGui.InputText(label, ref v, 400)) return false;
        set(v); return true;
    }
}
