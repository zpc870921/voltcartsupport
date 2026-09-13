using System.ComponentModel;

namespace customersupport.Tools
{
    public static class TechnicalTools
    {
        [Description("Searches the VoltCart product knowledge base for troubleshooting information.")]
        public static string SearchKnowledgeBase([Description("the search query,for example: 'smart speaker wifi setup'")] string query)
        {
            var articles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["wifi"] = """
                KB-101: Smart Speaker WiFi Setup
                1. Press and hold the reset button for 5 seconds
                2. Open the VoltCart app and go to Settings > Devices > Add New.
                3. Select your WiFi network and enter the password.
                4. Wait for the speaker to confirm connection with a chime.
                If issues persist, ensure your router supports 2.4GHz - the speaker does not support 5GHz networks
                """,

                ["headphones"] = """
                KB-102: Wireless Headphones Troubleshooting
                - No sound: Ensure the headphones are charged. Try unpairing and re-pairing via Bluetooth settings
                - Static or crackling: Move closer to the connected device. Interference from other Bluetooth devices can cause
                - Will not charge: Try a different USB-C cable. If the LED does not light up after 10 minutes, contact support.
                """,

                ["laptop stand"] = """
                KB-103: Laptop Stand Assembly
                The stand ships in 3 pieces: base, arm, and platform.
                1. Attach the arm to the base.
                2. Slide the platform onto the arm until it clicks.
                3. Adjust height using the lever on the back of the arm.
                """
            };
            var match = articles.FirstOrDefault(article => query.Contains(article.Key, StringComparison.OrdinalIgnoreCase));
            return match.Value ?? $"No knowledge base articles found for '{query}'.consider creating a support ticket.";
        }

        [Description("Creates a support ticket for issues that cannot be resolved through troubleshooting. ")]
        public static string CreateSupportTicket(
            [Description("brief description of the issue")] string issueDescription, 
            [Description("the product involved,if applicable")] string? product = null)
        {

            var ticketId = $"TKT-{Random.Shared.Next(10000, 99999)}";
            var productSuffix = product is null ? string.Empty : $" Product: {product}.";
            return $"Support ticket {ticketId} created. Issue: {issueDescription}. {productSuffix} A support engineer will follow up within 24 hours. ";
        }
    }
}
