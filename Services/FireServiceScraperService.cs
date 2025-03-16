using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using FireIncidents.Models;
using Microsoft.Extensions.Logging;
using System.Web;
using System.Text;
using System.Linq;
using System.IO;

namespace FireIncidents.Services
{
    public class FireServiceScraperService
    {
        private readonly ILogger<FireServiceScraperService> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _url = "https://museum.fireservice.gr/symvanta/";

        public FireServiceScraperService(ILogger<FireServiceScraperService> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient("FireService");
            _httpClient.Timeout = TimeSpan.FromMinutes(2);
        }

        public async Task<List<FireIncident>> ScrapeIncidentsAsync()
        {
            try
            {
                _logger.LogInformation("Starting to scrape fire incidents...");
                string html = await GetHtmlAsync();

                // For debugging: save the HTML to a file
                var debugPath = Path.Combine(Directory.GetCurrentDirectory(), "debug_html.txt");
                File.WriteAllText(debugPath, html, Encoding.UTF8);
                _logger.LogInformation($"Saved HTML to {debugPath} for debugging");

                List<FireIncident> incidents = ParseIncidentsFromTabs(html);

                _logger.LogInformation($"Successfully scraped {incidents.Count} active incidents");

                // Log each incident for debugging
                foreach (var incident in incidents)
                {
                    _logger.LogInformation($"Incident: {incident.Category} - {incident.Status} - {incident.Region} - {incident.Municipality} - {incident.Location}");
                }

                return incidents;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while scraping fire incidents");
                return new List<FireIncident>();
            }
        }

        // Update the GetHtmlAsync method in FireServiceScraperService.cs to better handle encoding
        private async Task<string> GetHtmlAsync()
        {
            try
            {
                // Configure HttpClient
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml");
                _httpClient.DefaultRequestHeaders.Add("Accept-Language", "el-GR,el;q=0.9,en-US;q=0.8,en;q=0.7");

                _logger.LogInformation($"Sending request to {_url}");
                var response = await _httpClient.GetAsync(_url);

                _logger.LogInformation($"Received response: {response.StatusCode}");
                response.EnsureSuccessStatusCode();

                // Get the response content with the correct encoding for Greek characters
                var contentBytes = await response.Content.ReadAsByteArrayAsync();

                // Try to detect charset from content-type header
                var contentType = response.Content.Headers.ContentType;
                string charSet = contentType?.CharSet;

                _logger.LogInformation($"Content-Type: {contentType}, CharSet: {charSet}");

                // Default to UTF-8 if no charset is specified
                Encoding encoding = Encoding.UTF8;

                // Try to use the specified charset if available
                if (!string.IsNullOrEmpty(charSet))
                {
                    try
                    {
                        encoding = Encoding.GetEncoding(charSet);
                        _logger.LogInformation($"Using specified encoding: {encoding.WebName}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Could not use specified encoding {charSet}: {ex.Message}. Falling back to UTF-8");
                    }
                }

                string html = encoding.GetString(contentBytes);

                // Check if encoding might be wrong (common for Greek sites)
                bool encodingIssue = html.Contains("??????") ||
                                     html.Contains("Î") ||
                                     html.Contains("Ï") ||
                                     html.Contains("ΠΕΡΙΦΕΡΕΙΑ") == false ||
                                     html.Contains("�");

                if (encodingIssue)
                {
                    _logger.LogWarning("Detected possible encoding issues, trying alternative encodings");

                    // Try alternative encodings in order of likelihood
                    List<(string Name, Encoding Encoding)> alternativeEncodings = new List<(string, Encoding)>();

                    try
                    {
                        alternativeEncodings.Add(("Windows-1253 (Greek)", Encoding.GetEncoding(1253)));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Windows-1253 encoding not available: {ex.Message}");
                    }

                    try
                    {
                        alternativeEncodings.Add(("ISO-8859-7 (Greek)", Encoding.GetEncoding("iso-8859-7")));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"ISO-8859-7 encoding not available: {ex.Message}");
                    }

                    alternativeEncodings.Add(("UTF-8", Encoding.UTF8));

                    // Try each alternative encoding
                    foreach (var (name, altEncoding) in alternativeEncodings)
                    {
                        try
                        {
                            string altHtml = altEncoding.GetString(contentBytes);

                            // Basic check if this encoding works better
                            if (!altHtml.Contains("??????") &&
                                !altHtml.Contains("�") &&
                                altHtml.Contains("ΠΕΡΙΦΕΡΕΙΑ"))
                            {
                                _logger.LogInformation($"Found better encoding: {name}");
                                html = altHtml;
                                encoding = altEncoding;
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Error trying {name} encoding: {ex.Message}");
                        }
                    }
                }

                // For debugging: log a sample of the HTML to verify encoding
                if (html != null && html.Length > 200)
                {
                    _logger.LogDebug($"Sample of decoded HTML: {html.Substring(0, 200)}...");
                }

                // For debugging: save the HTML to a file
                var debugPath = Path.Combine(Directory.GetCurrentDirectory(), "debug_html.txt");
                File.WriteAllText(debugPath, html, encoding);
                _logger.LogInformation($"Saved HTML to {debugPath} for debugging");

                return html;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetHtmlAsync");
                throw;
            }
        }

        private List<FireIncident> ParseIncidentsFromTabs(string html)
        {
            var incidents = new List<FireIncident>();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            try
            {
                // Define the tab IDs for different categories
                var tabs = new Dictionary<string, string>
                {
                    { "L1", "ΔΑΣΙΚΕΣ ΠΥΡΚΑΓΙΕΣ" },   // Forest Fires
                    { "P1", "ΑΣΤΙΚΕΣ ΠΥΡΚΑΓΙΕΣ" },   // Urban Fires
                    { "Q1", "ΠΑΡΟΧΕΣ ΒΟΗΘΕΙΑΣ" }     // Assistance
                };

                // Define valid incident statuses (excluding ΛΗΞΗ/Completed)
                var validStatuses = new HashSet<string>
                {
                    "ΣΕ ΕΞΕΛΙΞΗ",          // In Progress
                    "ΜΕΡΙΚΟΣ ΕΛΕΓΧΟΣ",     // Partial Control
                    "ΠΛΗΡΗΣ ΕΛΕΓΧΟΣ"       // Full Control
                };

                // Process each tab
                foreach (var tab in tabs)
                {
                    string tabId = tab.Key;
                    string category = tab.Value;

                    _logger.LogInformation($"Processing tab {tabId} - {category}");

                    var tabContentNode = doc.GetElementbyId(tabId);
                    if (tabContentNode == null)
                    {
                        _logger.LogWarning($"Tab content node with ID '{tabId}' not found");
                        continue;
                    }

                    // Get all direct children of the tab content node
                    var childNodes = tabContentNode.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element || n.NodeType == HtmlNodeType.Text).ToList();

                    string currentStatus = null;

                    // Process each child node sequentially
                    for (int i = 0; i < childNodes.Count; i++)
                    {
                        var node = childNodes[i];

                        // If it's a text node, check if it defines a status section
                        if (node.NodeType == HtmlNodeType.Text)
                        {
                            string text = node.InnerText.Trim();

                            // Extract status from text like "ΣΕ ΕΞΕΛΙΞΗ (1)"
                            foreach (var status in validStatuses)
                            {
                                if (text.StartsWith(status))
                                {
                                    _logger.LogInformation($"Found status section: {text}");
                                    currentStatus = status;
                                    break;
                                }
                            }

                            // If it's "ΛΗΞΗ" (Completed), ignore this section
                            if (text.StartsWith("ΛΗΞΗ"))
                            {
                                _logger.LogInformation("Found ΛΗΞΗ section, skipping");
                                currentStatus = null;
                            }

                            continue;
                        }

                        // If we have a valid status and this node is a panel (incident)
                        if (currentStatus != null &&
                            node.NodeType == HtmlNodeType.Element &&
                            node.Name == "div" &&
                            (node.GetAttributeValue("class", "").Contains("panel-red") ||
                             node.GetAttributeValue("class", "").Contains("panel-yellow") ||
                             node.GetAttributeValue("class", "").Contains("panel-green")))
                        {
                            _logger.LogInformation($"Found incident panel for status: {currentStatus}");

                            var incident = ExtractIncidentDetails(node, currentStatus, category);
                            if (incident != null)
                            {
                                incidents.Add(incident);
                            }
                        }
                    }
                }

                return incidents;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing HTML tabs");
                return incidents;
            }
        }

        private FireIncident ExtractIncidentDetails(HtmlNode panelNode, string status, string category)
        {
            try
            {
                // Get the panel-heading div
                var headingDiv = panelNode.SelectSingleNode(".//div[contains(@class, 'panel-heading')]");
                if (headingDiv == null)
                {
                    _logger.LogWarning("Could not find panel-heading div in incident panel");
                    return null;
                }

                // Get the table in the heading
                var table = headingDiv.SelectSingleNode(".//table");
                if (table == null)
                {
                    _logger.LogWarning("Could not find table in panel-heading");
                    return null;
                }

                // Extract location information from the table's first row
                var firstRow = table.SelectSingleNode(".//tr");
                if (firstRow == null)
                {
                    _logger.LogWarning("Could not find row in table");
                    return null;
                }

                var cells = firstRow.SelectNodes(".//td");
                if (cells == null || cells.Count < 2)
                {
                    _logger.LogWarning($"Expected at least 2 cells in row, found {cells?.Count ?? 0}");
                    return null;
                }

                // First cell contains region, municipality, and location
                var locationCell = cells[0];
                var locationHtml = locationCell.InnerHtml;

                _logger.LogDebug($"Location cell HTML: {locationHtml}");

                // Split by <br> and <br/> tags
                string[] locationParts = Regex.Split(locationHtml, @"<br\s*/?>");

                string region = "";
                string municipality = "";
                string location = "";

                if (locationParts.Length >= 1)
                {
                    region = CleanHtml(locationParts[0]);
                }

                if (locationParts.Length >= 2)
                {
                    municipality = CleanHtml(locationParts[1]);
                }

                if (locationParts.Length >= 3)
                {
                    // Location is often in bold
                    var match = Regex.Match(locationParts[2], @"<b>(.*?)</b>");
                    if (match.Success)
                    {
                        location = match.Groups[1].Value.Trim();
                    }
                    else
                    {
                        location = CleanHtml(locationParts[2]);
                    }
                }

                // Second cell contains the start date
                var dateCell = cells[1];
                string startDate = "";

                var dateMatch = Regex.Match(dateCell.InnerHtml, @"ΕΝΑΡΞΗ\s*<b>(.*?)</b>");
                if (dateMatch.Success)
                {
                    startDate = dateMatch.Groups[1].Value.Trim();
                }
                else
                {
                    startDate = CleanHtml(dateCell.InnerText).Replace("ΕΝΑΡΞΗ", "").Trim();
                }

                // Extract last update information from the end of the panel heading
                string lastUpdate = "";
                string headingText = headingDiv.InnerText;
                var updateMatch = Regex.Match(headingText, @"Τελευταία Ενημέρωση(.+)");
                if (updateMatch.Success)
                {
                    lastUpdate = updateMatch.Groups[1].Value.Trim();
                }

                return new FireIncident
                {
                    Status = status,
                    Category = category,
                    Region = region,
                    Municipality = municipality,
                    Location = location,
                    StartDate = startDate,
                    LastUpdate = lastUpdate
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting incident details from panel");
                return null;
            }
        }

        private string CleanHtml(string html)
        {
            if (string.IsNullOrEmpty(html))
                return string.Empty;

            // Remove all HTML tags
            string text = Regex.Replace(html, @"<[^>]+>", "");
            // Decode HTML entities
            text = HttpUtility.HtmlDecode(text);
            // Normalize whitespace
            text = Regex.Replace(text, @"\s+", " ");
            return text.Trim();
        }
    }
}