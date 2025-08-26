using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;

namespace FireIncidents.Controllers
{
    public class CivilProtectionMapController : Controller
    {

        // Not Ideal to put the logi inside the controller , but for simplicity, we will do it here.

        [HttpGet]
        public async Task<IActionResult> GetLatestMapImageUrl()
        {
            const string baseUrl = "https://civilprotection.gov.gr";
            const string pageUrl = "https://civilprotection.gov.gr/en/xartis";

            using var httpClient = new HttpClient();
            var html = await httpClient.GetStringAsync(pageUrl);

            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);

            var imgNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@id='arxeio-xartwn']//img");

            string imageUrl = null;
            if (imgNode != null)
            {
                var relativeSrc = imgNode.GetAttributeValue("src", "");
                if (!string.IsNullOrWhiteSpace(relativeSrc))
                {
                    imageUrl = baseUrl + relativeSrc;
                }
            }

            return Json(new { imageUrl });
        }
    }
}
