using System.ComponentModel;

namespace customersupport.Tools
{
    public static class CustomerTools
    {
        [Description("Retrieves customer information by customer ID or email address.")]
        public static string GetCustomerInfo([Description("The customer ID or email address")] string identifier)
        {

            return identifier.ToLowerInvariant() switch
            {
                "cust-001" or "alice@example.com" => "Customer:Alice Johnson(CUST-001),alice@example.com,member since 2024.3 orders on file",
                "cust-002" or "bob@example.com" => "Customer:Bob Smith(CUST-002),bob@example.com,member since 2025.1 orders on file",
                _ => $"No Customer found for '{identifier}'.ask the customer to verify their email or customer ID."
            };
        }
    }
}
