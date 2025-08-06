using System.Text.RegularExpressions;

namespace Shamir.Ceremony.Common.Tests
{
    [TestClass]
    public sealed class ValidationHelperTests
    {
        [TestMethod]
        [DataRow("555-1234", true)]
        [DataRow("(555) 123-4567", true)]
        [DataRow("+1-555-123-4567", true)]
        [DataRow("555 123 4567", true)]
        [DataRow("invalid-phone", false)]
        [DataRow("", false)]
        [DataRow("12", false)]
        [DataRow(null, false)]
        public void IsValidPhoneNumber_ShouldValidateCorrectly(string? phone, bool expected)
        {
            var result = ValidationHelper.IsValidPhoneNumber(phone);
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        [DataRow("John Doe", true)]
        [DataRow("Mary-Jane Smith", true)]
        [DataRow("O'Connor", true)]
        [DataRow("Jean-Luc", true)]
        [DataRow("", false)]
        [DataRow("John123", false)]
        [DataRow("John@Doe", false)]
        [DataRow(null, false)]
        public void IsValidName_ShouldValidateCorrectly(string? name, bool expected)
        {
            var result = ValidationHelper.IsValidName(name);
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        [DataRow("test@example.com", true)]
        [DataRow("user.name@domain.co.uk", true)]
        [DataRow("user+tag@example.org", true)]
        [DataRow("invalid-email", false)]
        [DataRow("@example.com", false)]
        [DataRow("test@", false)]
        [DataRow("", false)]
        [DataRow(null, false)]
        public void IsValidEmail_ShouldValidateCorrectly(string? email, bool expected)
        {
            var result = ValidationHelper.IsValidEmail(email);
            Assert.AreEqual(expected, result);
        }
    }

    internal static class ValidationHelper
    {
        public static bool IsValidPhoneNumber(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone) || phone.Length > 20)
                return false;

            var pattern = @"^[\d\s\+\-\(\)]+$";
            return Regex.IsMatch(phone, pattern) && 
                   Regex.IsMatch(phone, @"\d{3,}");
        }

        public static bool IsValidName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var pattern = @"^[a-zA-Z\s\-']+$";
            return Regex.IsMatch(name, pattern);
        }

        public static bool IsValidEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email) || email.Length > 254)
                return false;

            try
            {
                var pattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
                return Regex.IsMatch(email, pattern);
            }
            catch
            {
                return false;
            }
        }
    }
}
