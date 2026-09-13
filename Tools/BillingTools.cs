using System.ComponentModel;

namespace customersupport.Tools
{
    public static class BillingTools
    {
        [Description("Retrieves the invoice for a given order ID")]
        public static string GetInvoice([Description("The order ID for which to retrieve the invoice")] string orderId)
        {
            var invoices = new Dictionary<string, string>
            {
                ["ORD-1001"] = "Invoice INV-5001:Wireless Headphones - $79.99,tax $6.4,total $86.39. Paid via Visa ending in 4242.",
                ["ORD-1002"] = "Invoice INV-5002:Smart Speaker - $129.99, USB-C Cable - $12.99 - subtotal $142.98,tax $11.44,total $154.42. Paid via PayPal.",
                ["ORD-1003"] = "Invoice INV-5003: Laptop Stand - $49.99, Tax $4.00, Total $53.99. Paid via Visa ending in 1234."
            };

            return invoices.TryGetValue(orderId, out var invoice) ? invoice : $"No invoice found for order {orderId}. Please check the order ID and try again.";
        }

        [Description("Processes a refund for and order after approval has been granted.")]
        public static string ProcessRefund([Description("The order ID to refund")] string orderId, [Description("the reason for the refund")]string reason)
        {
            return $"refund initiated for order {orderId}.reason:{reason}.the refund will appear on your statement within 5-10 business days.";
        }

        [Description("gets the payment history for the current customer")]
        public static string GetPaymentHistory()
        {
            return """
                recent payments:
                - March 1:$86.39 (ORD-1001,Visa endding 4242)
                - March 3:$154.42 (ORD-1002,PayPal)
                - March 5:$53.99 (ORD-1003,Visa ending 1234)
                """;
        }
    }
}
