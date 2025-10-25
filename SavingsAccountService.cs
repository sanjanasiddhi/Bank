using Banking;
using System;
using System.Collections.Generic;
using System.Data.Entity.Validation;
using System.Linq;
namespace BankingServices.Services
{
    public interface ISavingsAccountService
    {
        string GenerateAccountId();
        bool CustomerHasSBAccount(string customerId);
        string CreateSavingsAccount(string customerId, decimal initialDeposit, int createdBy, out string message);
    }

    public class SavingsAccountService : ISavingsAccountService
    {
        private readonly BankEntities _context;

        public SavingsAccountService(BankEntities context)
        {
            _context = context;
        }

        public string GenerateAccountId()
        {
            var lastAccount = _context.SavingsAccounts
                                      .OrderByDescending(sa => sa.SBAccountId)
                                      .FirstOrDefault();

            int nextNumber = 1;

            if (lastAccount != null)
            {
                string numericPart = lastAccount.SBAccountId.Substring(2);
                int.TryParse(numericPart, out nextNumber);
                nextNumber += 1;
            }

            return "SB" + nextNumber.ToString("D5");
        }


        public bool CustomerHasSBAccount(string customerId)
        {
            return _context.SavingsAccounts.Any(sa => sa.CustomerId == customerId);
        }


        public string CreateSavingsAccount(string customerId, decimal initialDeposit, int createdBy, out string message)
        {

            if (string.IsNullOrWhiteSpace(customerId))
            {
                message = "Customer ID cannot be empty.";
                return null;
            }

            var customer = _context.Customers.FirstOrDefault(c => c.CustomerId == customerId);
            if (customer == null)
            {
                message = "Customer not found with the given ID.";
                return null;
            }


            if (!string.IsNullOrEmpty(customer.Approval) &&
                (customer.Approval.Equals("Employee", StringComparison.OrdinalIgnoreCase) ||
                 customer.Approval.Equals("Manager", StringComparison.OrdinalIgnoreCase)))
            {
                message = "Employees and Managers cannot open a Savings Account.";
                return null;
            }


            if (_context.SavingsAccounts.Any(sa => sa.CustomerId == customerId))
            {
                message = "Customer already has a Savings Account.";
                return null;
            }


            if (initialDeposit < 1000)
            {
                message = "Initial deposit must be at least ₹1000.";
                return null;
            }

            string accountId;
            do
            {
                accountId = "SB" + new Random().Next(10000, 99999);
            } while (_context.Accounts.Any(a => a.AccountId == accountId));


            var account = new Account
            {
                AccountId = accountId,
                CustomerId = customerId,
                CreatedBy = createdBy,
                OpenDate = DateTime.Now,
                AccountStatus = "Active"
            };


            var sbAccount = new SavingsAccount
            {
                SBAccountId = accountId,
                CustomerId = customerId,
                Balance = Math.Round(initialDeposit, 2)
            };

            try
            {
                _context.Accounts.Add(account);
                _context.SavingsAccounts.Add(sbAccount);
                _context.SaveChanges();
            }
            catch (DbEntityValidationException ex)
            {
                var errors = ex.EntityValidationErrors
                    .SelectMany(eve => eve.ValidationErrors)
                    .Select(ve => $"{ve.PropertyName}: {ve.ErrorMessage}");
                message = "Validation failed: " + string.Join("; ", errors);
                return null;
            }
            catch (Exception ex)
            {
                message = "An unexpected error occurred: " + ex.Message;
                return null;
            }

            message = $"Savings Account created successfully!!! Customer ID: {customerId}, Account ID: {accountId}";
            return accountId;
        }



        public (SavingsAccount sbAccount, string message, List<Account> activeAccounts, DateTime? openDate) FetchSBAccountForClosing(string sbAccountId)
        {
            string msg = null;
            List<Account> activeAccounts = new List<Account>();
            DateTime? openDate = null;

            var sbAccount = _context.SavingsAccounts.FirstOrDefault(sa => sa.SBAccountId == sbAccountId);
            if (sbAccount == null)
            {
                msg = "SB Account not found!";
                return (null, msg, activeAccounts, openDate);
            }

            var account = _context.Accounts.FirstOrDefault(a => a.AccountId == sbAccountId);
            if (account != null)
                openDate = account.OpenDate;

         
            activeAccounts = _context.Accounts
                .Where(a => a.CustomerId == sbAccount.CustomerId && a.AccountId != sbAccountId && a.AccountStatus == "Active")
                .ToList();

            return (sbAccount, msg, activeAccounts, openDate);
        }

 
        public bool CloseSBAccount(string sbAccountId, out string message)
        {
            var sbAccount = _context.SavingsAccounts.FirstOrDefault(sa => sa.SBAccountId == sbAccountId);
            if (sbAccount == null)
            {
                message = "SB Account not found!";
                return false;
            }

            try
            {
              
                _context.SavingsAccounts.Remove(sbAccount);

          
                var account = _context.Accounts.FirstOrDefault(a => a.AccountId == sbAccountId);
                if (account != null)
                    account.AccountStatus = "Closed";

                _context.SaveChanges();
                message = $"SB Account {sbAccountId} closed successfully!";
                return true;
            }
            catch (Exception ex)
            {
                message = "Error closing SB account: " + ex.Message;
                return false;
            }
        }

    
        


    }

}
