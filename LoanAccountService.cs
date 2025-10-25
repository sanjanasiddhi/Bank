using Banking;
using BankingService.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BankingServices.Services
{
    public class LoanAccountService
    {
        private readonly ManagerService _managerService;
        private readonly BankEntities _context = new BankEntities(); 

        public LoanAccountService(ManagerService managerService)
        {
            _managerService = managerService;
        }

        // Process Loan: validation + EMI calculation
        public LoanAccount ProcessLoan(LoanAccount loan, Customer customer, decimal monthlyTakeHome, out string errorMessage, out decimal emi)
        {
            errorMessage = "";
            emi = 0;

            int age = _managerService.CalculateAge((DateTime)customer.CustomerDOB);
            bool isSenior = age >= 60;

            // Validations
            if (loan.LoanAmount < 10000)
            {
                errorMessage = "Minimum loan amount is ₹10,000.";
                return null;
            }

            if (isSenior && loan.LoanAmount > 100000)
            {
                errorMessage = "Senior citizens cannot take loans above ₹1,00,000.";
                return null;
            }

            decimal interest = isSenior ? 9.5m :
                               loan.LoanAmount <= 500000 ? 10m :
                               loan.LoanAmount <= 1000000 ? 9.5m : 9m;

            decimal totalPayable = (decimal)(loan.LoanAmount * (1 + (interest / 100) * loan.TimePeriod));
            emi = (decimal)(totalPayable / (loan.TimePeriod * 12));

            if (emi > monthlyTakeHome * 0.6m)
            {
                errorMessage = "EMI exceeds 60% of monthly take-home.";
                return null;
            }

            // Set Loan details
            loan.Interest = interest;
            loan.TotalPayable = totalPayable;
            loan.DueAmount = totalPayable;
            loan.Emi = emi;
            loan.StartDate = DateTime.Now;

            return loan;
        }

        // Add Loan Account to database (Account + LoanAccount)
        public void AddLoanAccount(Account account, LoanAccount loanAccount)
        {
            _context.Accounts.Add(account);
            _context.LoanAccounts.Add(loanAccount);
            _context.SaveChanges();
        }

        // Get all active loan accounts for dropdown
        public List<LoanAccount> GetActiveLoanAccounts()
        {
            return _context.LoanAccounts
                           .Where(l => l.AccountStatus == "Active")
                           .OrderBy(l => l.LNAccountId)
                           .ToList();
        }
        // Method to get all active loan account IDs
        public List<string> GetActiveLoanAccountIds()
        {
            return _context.LoanAccounts
                           .Where(l => l.AccountStatus == "Active")
                           .OrderBy(l => l.LNAccountId)
                           .Select(l => l.LNAccountId)
                           .ToList();
        }

        // Get loan account details by ID
        public LoanAccount GetLoanAccountById(string lnAccountId)
        {
            return _context.LoanAccounts.FirstOrDefault(l => l.LNAccountId == lnAccountId);
        }

        // Close loan account
        public string CloseLoanAccount(string lnAccountId)
        {
            var loan = _context.LoanAccounts.FirstOrDefault(l => l.LNAccountId == lnAccountId);
            if (loan == null)
                return "❌ Loan account not found.";

            if (loan.DueAmount > 0)
                return $"❌ Cannot close account. Pending amount: ₹{Math.Round((decimal)loan.DueAmount, 2)}";

            var account = _context.Accounts.FirstOrDefault(a => a.AccountId == lnAccountId);
            if (account != null)
            {
                account.AccountStatus = "Closed";
            }

            loan.AccountStatus = "Closed";

            _context.SaveChanges();
            return "✅ Loan account closed successfully!";
        }
    }
}
