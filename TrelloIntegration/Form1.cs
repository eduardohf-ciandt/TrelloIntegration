using System;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using TechTalk.JiraRestClient;
using TrelloNet;

namespace TrelloIntegration
{
    public partial class Form1 : Form
    {
        private static string JiraUrl
        {
            get { return ConfigurationManager.AppSettings.Get("jiraUrl"); }
        }

        private static string AppKey
        {
            get { return ConfigurationManager.AppSettings.Get("trelloAppKey"); }
        }

        private static string UserToken
        {
            get { return ConfigurationManager.AppSettings.Get("trelloUserToken"); }
        }

        private static string Jql
        {
            get { return ConfigurationManager.AppSettings.Get("jiraFilter"); }
        }

        private static string User
        {
            get { return ConfigurationManager.AppSettings.Get("jiraUser"); }
        }

        private static string Password
        {
            get { return Base64Decode(ConfigurationManager.AppSettings.Get("jiraPassword")); }
        }

        private static string BoardName
        {
            get { return ConfigurationManager.AppSettings.Get("trelloBoardName"); }
        }

        private static string BacklogName
        {
            get { return ConfigurationManager.AppSettings.Get("trelloBacklogName"); }
        }

        

        public Form1()
        {
            InitializeComponent();
        }

        // ReSharper disable PossibleMultipleEnumeration
        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                var jira = new JiraClient(JiraUrl, User, Password);
                var jiraIssues = jira.GetIssuesByQuery("USPL", "", Jql);

                var trello = new Trello(AppKey);
                trello.Authorize(UserToken);

                var trelloBoard = trello.Boards.ForMe().FirstOrDefault(b => b.Name == BoardName);
                var trelloLists = trello.Lists.ForBoard(trelloBoard);
                var trelloCards = trello.Cards.ForBoard(trelloBoard);

                var backlogList = trelloLists.FirstOrDefault(b => b.Name == BacklogName);

                foreach (var jiraIssue in jiraIssues)
                {
                    var key = jiraIssue.key.ToLowerInvariant();
                    var cardTrello = trelloCards.FirstOrDefault(c => c.Name.ToLowerInvariant().Contains(key));

                    if (cardTrello == null)
                    {
                        Console.WriteLine(@"Creating {0}.", jiraIssue.key);

                        var name = string.Format("({0}) {1} - {2}", jiraIssue.fields.issuetype.name,
                            key.ToUpperInvariant(), jiraIssue.fields.summary);
                        var defaultSprint = backlogList;

                        if (!String.IsNullOrEmpty(jiraIssue.fields.status.name))
                        {
                            name = string.Format("({0}) {1} - {2} [{3}]", jiraIssue.fields.issuetype.name,
                                key.ToUpperInvariant(), jiraIssue.fields.summary, jiraIssue.fields.status.name);
                        }

                        foreach (var sprint in jiraIssue.fields.customfield_10007)
                        {
                            if (GetSprintState(sprint) == "CLOSED") continue;
                            var sprintList =
                                trelloLists.FirstOrDefault(
                                    l => l.Name.ToLowerInvariant().Equals(GetSprintName(sprint).ToLowerInvariant()));
                            if (sprintList == null) continue;
                            defaultSprint = sprintList;
                        }

                        var newCard = trello.Cards.Add(name, defaultSprint);

                        if (checkBox1.Checked)
                            trello.Cards.AddLabel(newCard, Color.Red);

                        switch (jiraIssue.fields.issuetype.name.ToLowerInvariant())
                        {
                            case "technical task":
                            case "task":
                                trello.Cards.AddLabel(newCard, Color.Blue);
                                break;
                            default:
                                trello.Cards.AddLabel(newCard, Color.Sky);
                                break;
                        }

                        switch (jiraIssue.fields.status.name.ToLowerInvariant())
                        {
                            case "awaiting po acceptance":
                            case "po signed off for production":
                            case "in po acceptance":
                                trello.Cards.AddLabel(newCard, Color.Yellow);
                                trello.Cards.AddLabel(newCard, Color.Green);
                                break;
                            case "ready for development":
                            case "in development":
                            case "in qa":
                            case "awaiting qa":
                                trello.Cards.AddLabel(newCard, Color.Yellow);
                                break;
                        }

                        trello.Cards.AddComment(newCard, "Imported from Jira");
                    }
                    else
                    {
                        Console.WriteLine(@"Updating {0}.", jiraIssue.key);
                        var updated = false;

                        if (!String.IsNullOrEmpty(jiraIssue.fields.status.name))
                        {
                            var name = string.Format("({0}) {1} - {2} [{3}]", jiraIssue.fields.issuetype.name,
                                key.ToUpperInvariant(), jiraIssue.fields.summary, jiraIssue.fields.status.name);

                            if (cardTrello.Name != name)
                            {
                                cardTrello.Name = name;
                                updated = true;
                            }
                        }

                        if (jiraIssue.fields.updated != null)
                        {
                            var lastActivity = jiraIssue.fields.updated.Value;

                            if (cardTrello.DateLastActivity != lastActivity)
                            {
                                cardTrello.DateLastActivity = lastActivity;
                                updated = true;
                            }
                        }

                        var originalSprint = cardTrello.IdList;

                        foreach (
                            var sprintList 
                            in jiraIssue.fields.customfield_10007
                            .Where(sprint => GetSprintState(sprint) != "CLOSED")
                            // ReSharper disable once PossibleMultipleEnumeration
                            .Select(sprint => trelloLists.FirstOrDefault(l => l.Name.ToLowerInvariant().Equals(GetSprintName(sprint).ToLowerInvariant())))
                            .Where(sprintList => sprintList != null && cardTrello.IdList != sprintList.Id))
                        {
                            cardTrello.IdList = sprintList.Id;
                            updated = true;
                        }

                        if (updated)
                        {
                            trello.Cards.Update(cardTrello);

                            foreach (var label in cardTrello.Labels)
                            {
                                try
                                {
                                    trello.Cards.AddLabel(cardTrello, label.Color);
                                }
                                catch (TrelloException ex)
                                {
                                    if (ex.Message.Contains("that label is already on the card"))
                                        continue;

                                    throw;
                                }
                            }

                            if (cardTrello.IdList != originalSprint)
                            {
                                try
                                {
                                    if (!cardTrello.LabelColors.Contains(Color.Red))
                                        trello.Cards.AddLabel(cardTrello, Color.Red);
                                }
                                catch (TrelloException ex)
                                {
                                    if (!ex.Message.Contains("that label is already on the card"))
                                        throw;
                                }
                            }

                            trello.Cards.AddComment(cardTrello, "Updated from Jira");
                        }

                        switch (jiraIssue.fields.status.name.ToLowerInvariant())
                        {
                            case "awaiting po acceptance":
                            case "po signed off for production":
                            case "in po acceptance":
                                if (!cardTrello.LabelColors.Contains(Color.Yellow))
                                    trello.Cards.AddLabel(cardTrello, Color.Yellow);
                                if (!cardTrello.LabelColors.Contains(Color.Green))
                                    trello.Cards.AddLabel(cardTrello, Color.Green);
                                break;
                            case "ready for development":
                            case "in development":
                            case "in qa":
                            case "awaiting qa":
                                if (!cardTrello.LabelColors.Contains(Color.Yellow))
                                    trello.Cards.AddLabel(cardTrello, Color.Yellow);
                                break;
                        }
                    }
                }

                var removedTrelloCards = trelloCards.Where(tc => tc.LabelColors.Contains(Color.Sky) && jiraIssues.Count(j => tc.Name.ToLowerInvariant().Contains(j.key.ToLowerInvariant())) == 0);

                foreach (var removedTrelloCard in removedTrelloCards)
                {
                    if (removedTrelloCard.Labels.FirstOrDefault(t => t.Color == Color.Red) == null)
                        trello.Cards.AddLabel(removedTrelloCard, Color.Red);
                    trello.Cards.AddComment(removedTrelloCard, "Removed from Backlog in Jira");
                }

                MessageBox.Show(@"RoadMap updated.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("Error. Message: {0}.", ex.Message));
            }
        }

        private string GetSprintState(string sprint)
        {
            return sprint.Substring(sprint.IndexOf(",state=", StringComparison.Ordinal) + 7, sprint.IndexOf(",name=", StringComparison.Ordinal) - sprint.IndexOf(",state=", StringComparison.Ordinal) - 7);

        }

        private string GetSprintName(string sprint)
        {
            var sprintName = sprint.Substring(sprint.IndexOf(",name=", StringComparison.Ordinal) + 6, sprint.IndexOf(",startDate", StringComparison.Ordinal) - sprint.IndexOf(",name=", StringComparison.Ordinal) - 6);
            sprintName = sprintName.Remove(0, sprintName.IndexOf("-", StringComparison.Ordinal) + 1);
            return sprintName.Trim();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            var trello = new Trello(AppKey);
            trello.Authorize(UserToken);

            var trelloBoard = trello.Boards.ForMe().FirstOrDefault(b => b.Name == "Gladiators RoadMap");
            var trelloCards = trello.Cards.ForBoard(trelloBoard);

            foreach (var trelloCard in trelloCards)
            {
                trello.Cards.Delete(trelloCard);
            }

            MessageBox.Show(@"RoadMap cleaned.");
        }

        private void button3_Click(object sender, EventArgs e)
        {
            var testDialog = new Form2();
            testDialog.ShowDialog(this);
            testDialog.Dispose();
        }

        public static string Base64Decode(string base64EncodedData)
        {
            var base64EncodedBytes = Convert.FromBase64String(base64EncodedData);
            return Encoding.UTF8.GetString(base64EncodedBytes);
        }
    }
}
