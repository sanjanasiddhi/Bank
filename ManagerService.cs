using Banking;
using BankingServices.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Entity.Validation;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using System.Web.WebPages.Html;
using iTextSharp.text;
using iTextSharp.text.pdf;


namespace BankingService.Services
{
    public class ManagerService
    {
        private readonly BankEntities _context;
        public ManagerService()
        {
            _context = new BankEntities();
            _savingsService = new SavingsAccountService(_context);
         

        }
        private readonly SavingsAccountService _savingsService;
        private readonly LoanAccountService _loanService;
   

        public List<PendingApprovalViewModel> GetPendingApprovals()
        {
            var pendingCustomers = _context.Customers
            .Where(c => c.Approval == "Pending")
            .Select(c => new PendingApprovalViewModel
            {
                Type = "Customer",
                Name = c.CustomerName,
                DepartmentID = c.CustomerEmail,
                PAN = c.PAN
            });
            var pendingEmployees = _context.Employees
            .Where(e => e.Approval == "Pending")
            .Select(e => new PendingApprovalViewModel
            {
                Type = "Employee",
                Name = e.EmployeeName,
                DepartmentID = e.DepartmentId,
                PAN = e.PAN
            });
            return pendingCustomers.Concat(pendingEmployees).ToList();
        }
        public void ApproveUser(string pan)
        {
            var customer = _context.Customers.FirstOrDefault(c => c.PAN == pan);
            if (customer != null)
            {
                customer.Approval = "Approved";
                _context.SaveChanges();
                return;
            }
            var employee = _context.Employees.FirstOrDefault(e => e.PAN == pan);
            if (employee != null)
            {
                employee.Approval = "Approved";
                _context.SaveChanges();
            }
        }
        public bool AddEmployeeWithLogin(Employee model, string password)
        {
            try
            {

                var random = new Random();
                string employeeId;


                do
                {
                    employeeId = random.Next(10000, 99999).ToString();
                }
                while (_context.Employees.Any(e => e.EmployeeId == employeeId));

                model.EmployeeId = employeeId;
                model.Approval = "Pending";

                _context.Employees.Add(model);


                var login = new LoginData
                {
                    UserName = model.EmployeeName,
                    UserPassword = password,
                    UserRole = "Employee",
                    RefID = model.EmployeeId
                };

                _context.LoginDatas.Add(login);
                _context.SaveChanges();
                return true;
            }
            catch
            {
                return false;
            }
        }
        public List<Customer> GetAllCustomers()
        {
            return _context.Customers.ToList();
        }

        public List<SelectListItem> GetApprovedEmployeesForDropdown()
        {

            var employees = _context.Employees
                .Where(e => e.Approval == "Approved")
                .Select(e => new { e.EmployeeId, e.EmployeeName })
                .ToList();


            return employees.Select(e => new SelectListItem
            {
                Value = e.EmployeeId,
                Text = $"{e.EmployeeName} ({e.EmployeeId})"
            }).ToList();
        }

        public bool DeleteEmployeeWithLogin(string employeeId)
        {
            var employee = _context.Employees.FirstOrDefault(e => e.EmployeeId == employeeId);
            if (employee == null) return false;

            var login = _context.LoginDatas.FirstOrDefault(l => l.RefID == employeeId && l.UserRole == "Employee");
            if (login != null)
            {
                _context.LoginDatas.Remove(login);
            }

            _context.Employees.Remove(employee);
            _context.SaveChanges();
            return true;
        }



        public Customer GetCustomerById(string customerId)
        {
            return _context.Customers.FirstOrDefault(c => c.CustomerId == customerId);
        }



        public string GenerateCustomerId()
        {
            int count = _context.Customers.Count() + 1;
            return "CUS" + count.ToString("D5");
        }

        public bool AddCustomerWithLogin(Customer model, string password)
        {
            try
            {
                int count = _context.Customers.Count() + 1;
                string customerId = "CUS" + count.ToString("D5");

                while (_context.Customers.Any(c => c.CustomerId == customerId))
                {
                    count++;
                    customerId = "CUS" + count.ToString("D5");
                }
                model.CustomerId = customerId;
                model.Approval = "Pending";

                model.CustomerId = customerId;

                _context.Customers.Add(model);

                var login = new LoginData
                {
                    UserName = model.CustomerName,
                    UserPassword = password,
                    UserRole = "Customer",
                    RefID = customerId
                };

                _context.LoginDatas.Add(login);
                _context.SaveChanges();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("AddCustomerWithLogin failed: " + ex.Message);
                return false;
            }
        }


        public string CreateSavingsAccount(string customerId, decimal initialDeposit, int managerId, out string message)
        {
            return _savingsService.CreateSavingsAccount(customerId, initialDeposit, managerId, out message);
        }

        // Fetch SB account details for closing
        public (SavingsAccount sbAccount, string message, List<Account> activeAccounts, DateTime? openDate) FetchSBAccountForClosing(string sbAccountId)
        {
            return _savingsService.FetchSBAccountForClosing(sbAccountId);
        }

        // Close SB Account
        public bool CloseSBAccount(string sbAccountId, out string message)
        {
            if (string.IsNullOrWhiteSpace(sbAccountId))
            {
                message = "SB Account ID cannot be empty.";
                return false;
            }

            var result = FetchSBAccountForClosing(sbAccountId);

            if (result.sbAccount == null)
            {
                message = "SB Account not found!";
                return false;
            }

            // Cannot close if active Loan/FD accounts exist
            if (result.activeAccounts != null && result.activeAccounts.Count > 0)
            {
                message = "Cannot close SB Account with active Loan/FD accounts!";
                return false;
            }

            // Proceed to close account
            return _savingsService.CloseSBAccount(sbAccountId, out message);
        }

       



        public void PayEMI(string loanAccountId, decimal amount)
        {

            var loan = _context.LoanAccounts.FirstOrDefault(l => l.LNAccountId == loanAccountId);
            if (loan == null)
                throw new Exception("Loan account not found.");

            if (amount <= 0)
                throw new Exception("Payment amount must be greater than zero.");

            if (amount > loan.DueAmount)
                throw new Exception($"Payment exceeds remaining due amount: ₹{loan.DueAmount:F2}");


            loan.DueAmount -= amount;
            if (loan.DueAmount < 0) loan.DueAmount = 0;


            var transaction = new LoanTransaction
            {
                LNAccountId = loan.LNAccountId,
                Amount = amount,
                TransactionType = "EMI",
                TransactionDate = DateTime.Now,
                Penalty = 0
            };
            _context.LoanTransactions.Add(transaction);


            if (loan.DueAmount == 0)
            {
                loan.AccountStatus = "Inactive";

                var account = _context.Accounts.FirstOrDefault(a => a.AccountId == loan.LNAccountId);
                if (account != null)
                    account.AccountStatus = "Inactive";
            }

            _context.SaveChanges();
        }

        public bool DeleteCustomer(string customerId)
        {
            try
            {

                bool hasActiveAccounts = _context.Accounts.Any(a =>
                    a.CustomerId == customerId &&
                    a.AccountStatus == "Active");


                bool hasLoan = _context.LoanAccounts.Any(l => l.CustomerId == customerId);
                bool hasFD = _context.FixedDeposits.Any(fd => fd.CustomerId == customerId);
                bool hasSB = _context.SavingsAccounts.Any(sb => sb.CustomerId == customerId);

                if (hasActiveAccounts || hasLoan || hasFD || hasSB)
                {
                    return false;
                }


                var login = _context.LoginDatas.FirstOrDefault(l => l.RefID == customerId);
                if (login != null)
                {
                    _context.LoginDatas.Remove(login);
                }


                var customer = _context.Customers.FirstOrDefault(c => c.CustomerId == customerId);
                if (customer != null)
                {
                    _context.Customers.Remove(customer);
                }

                _context.SaveChanges();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("DeleteCustomer failed: " + ex.Message);
                return false;
            }
        }



        public void PayEMI(LoanAccount loan, decimal amount)
        {
            if (loan == null)
                throw new Exception("Loan account is null.");

            PayEMI(loan.LNAccountId, amount);
        }
        public ManagerService(BankEntities context)
        {
            _context = context;
            _savingsService = new SavingsAccountService(_context);
        }


       

        public SavingsAccount GetSBAccountDetails(string accountId)
        {
            return _context.SavingsAccounts.FirstOrDefault(a => a.SBAccountId == accountId);
        }




        

        public List<SavingsTransaction> GetTransactionsByAccount(string sbAccountId)
        {
            return _context.SavingsTransactions
                .Where(t => t.SBAccountId == sbAccountId)
                .OrderByDescending(t => t.TransactionDate)
                .ToList();
        }

        //---------------------------------------------------------------------------------------------------------------------------------------------
        //------------------------------------------------------------------------------------------------------------------------------------------------

        // Generate Loan Account ID
        public string GenerateLoanAccountId()
        {
            return "LN" + new Random().Next(10000, 99999);
        }

      
   

        // Calculate Age
        public int CalculateAge(DateTime dob)
        {
            var today = DateTime.Today;
            int age = today.Year - dob.Year;
            if (dob.Date > today.AddYears(-age)) age--;
            return age;
        }
    





        //------------------------------------------------------------------------------------------------------------------------------------
        public void CloseLoanAccount(LoanAccount loan)
        {
            if (loan == null) throw new Exception("Loan account is null.");


            if (loan.DueAmount > 0)
                throw new Exception($"Cannot close loan. Outstanding due: ₹{loan.DueAmount:F2}");


            var account = _context.Accounts.FirstOrDefault(a => a.AccountId == loan.LNAccountId);
            if (account != null)
            {
                account.AccountStatus = "Inactive";
            }

            loan.AccountStatus = "Inactive";
            _context.SaveChanges();
        }



       

        //----------------------------------------------------------------


        // -------------------------
        // Fetch Loan Details with Calculations
        // -------------------------

        public object GetLoanDetails(string loanAccountId)
        {

            var loan = _context.LoanAccounts
                .FirstOrDefault(l => l.LNAccountId == loanAccountId && l.AccountStatus == "Active");

            if (loan == null)
                return null;

            decimal totalPaid = _context.LoanTransactions
                .Where(t => t.LNAccountId == loanAccountId && t.TransactionType == "Payment")
                .Sum(t => (decimal?)t.Amount) ?? 0;


            decimal interestTillToday = (decimal)(loan.DueAmount - (loan.LoanAmount - totalPaid));

            return new
            {
                LoanAccountId = loan.LNAccountId,
                LoanAmount = loan.LoanAmount,
                InterestRate = loan.Interest,
                TotalPaid = Math.Round(totalPaid, 2),
                InterestTillToday = Math.Round(interestTillToday, 2),
                UpdatedDueAmount = Math.Round((decimal)loan.DueAmount, 2)
            };
        }



        // -------------------------
        // Foreclose Loan
        // -------------------------
        public (bool Success, string Message) ForecloseLoan(string loanAccountId, decimal amount)
        {

            var loan = _context.LoanAccounts.FirstOrDefault(l => l.LNAccountId == loanAccountId && l.AccountStatus == "Active");
            if (loan == null)
                return (false, "Loan not found or already closed.");

            if (!loan.StartDate.HasValue)
                return (false, "Loan Start Date is missing.");


            var totalPaid = _context.LoanTransactions
                .Where(t => t.LNAccountId == loanAccountId)
                .Sum(t => (decimal?)t.Amount) ?? 0;


            var daysPassed = (DateTime.Now.Date - loan.StartDate.Value.Date).Days;
            var interestTillToday = loan.LoanAmount * (loan.Interest / 100) * (daysPassed / 365.0m);


            var updatedDue = loan.LoanAmount + interestTillToday - totalPaid;

            if (amount < updatedDue)
                return (false, $"Payment amount is less than updated due ({Math.Round((decimal)updatedDue, 2)}). Please pay full amount to foreclose.");


            var transaction = new LoanTransaction
            {
                LNAccountId = loanAccountId,
                Amount = updatedDue,
                TransactionType = "Foreclose",
                TransactionDate = DateTime.Now,
                Penalty = 0
            };

            _context.LoanTransactions.Add(transaction);

            loan.DueAmount = 0;
            loan.AccountStatus = "Closed";

            try
            {
                _context.SaveChanges();
                return (true, "Loan foreclosed successfully.");
            }
            catch (System.Data.Entity.Validation.DbEntityValidationException ex)
            {

                var errors = ex.EntityValidationErrors
                    .SelectMany(eve => eve.ValidationErrors)
                    .Select(ve => $"{ve.PropertyName}: {ve.ErrorMessage}");

                var errorMessage = "Validation failed: " + string.Join("; ", errors);
                return (false, errorMessage);
            }
            catch (Exception ex)
            {

                return (false, "Error during foreclosure: " + ex.Message);
            }
        }


        public LoanAccount GetLoanById(string LNAccountId)
        {
            return _context.LoanAccounts.FirstOrDefault(l => l.LNAccountId == LNAccountId);
        }





        // -------------------------
        // Make EMI Payment
        // -------------------------
        public LoanAccount MakeEMIPayment(string LNAccountId)
        {
            if (string.IsNullOrWhiteSpace(LNAccountId))
                throw new Exception("Loan Account ID is required.");

            var loan = _context.LoanAccounts
                .FirstOrDefault(l => l.LNAccountId == LNAccountId && l.AccountStatus == "Active");

            if (loan == null)
                throw new Exception("Loan account not found or already closed.");


            if (loan.Emi <= 0)
            {
                var monthlyInterestRate = loan.Interest / 100 / 12;
                var months = (int)(loan.TimePeriod * 12);

                var emi = loan.LoanAmount * monthlyInterestRate *
                          (decimal)Math.Pow(1 + (double)monthlyInterestRate, months) /
                          (decimal)(Math.Pow(1 + (double)monthlyInterestRate, months) - 1);

                loan.Emi = Math.Round((decimal)emi, 2);
                loan.TotalPayable = Math.Round((decimal)(loan.Emi * months), 2);
                loan.DueAmount = loan.TotalPayable;
            }


            if (loan.DueAmount < loan.Emi)
                throw new Exception($"Remaining due amount ({loan.DueAmount:F2}) is less than EMI. Please contact manager.");

            loan.DueAmount = Math.Round((decimal)(loan.DueAmount - loan.Emi), 2);


            var transaction = new LoanTransaction
            {
                LNAccountId = LNAccountId,
                Amount = loan.Emi,
                TransactionType = "EMI",
                TransactionDate = DateTime.Now,
                Penalty = 0
            };
            _context.LoanTransactions.Add(transaction);


            if (loan.DueAmount <= 0)
            {
                loan.DueAmount = 0;
                loan.AccountStatus = "InActive";
            }

            _context.SaveChanges();

            return loan;
        }



        // -------------------------
        // Service method for Part Payment
        // -------------------------

        public (bool Success, string Message, decimal RemainingDue, decimal PaidAmount) PayPartEMI(string LNAccountId, decimal Amount)
        {
            var loan = _context.LoanAccounts.FirstOrDefault(l => l.LNAccountId == LNAccountId && l.AccountStatus == "Active");
            if (loan == null)
                return (false, "Loan account not found or inactive.", 0, 0);

            if (Amount < 500)
                return (false, "Payment must be at least ₹500.", loan.DueAmount ?? 0, 0);

            if (Amount > loan.DueAmount)
                return (false, $"Payment cannot exceed the due amount of ₹{loan.DueAmount:F2}.", loan.DueAmount ?? 0, 0);


            loan.DueAmount -= Amount;


            _context.LoanTransactions.Add(new LoanTransaction
            {
                LNAccountId = LNAccountId,
                Amount = Amount,
                TransactionType = "Part EMI",
                TransactionDate = DateTime.Now,
                Penalty = 0
            });


            if (loan.DueAmount <= 0)
            {
                loan.AccountStatus = "Closed";
                loan.DueAmount = 0;
            }

            _context.SaveChanges();

            return (true, $"Part EMI of ₹{Amount:F2} paid successfully.", loan.DueAmount ?? 0, Amount);
        }



        public List<LoanTransaction> GetLoanTransactionsByAccountId(string lnAccountId)
        {
            return _context.LoanTransactions
                           .Where(t => t.LNAccountId == lnAccountId)
                           .OrderByDescending(t => t.TransactionDate)
                           .ToList();
        }

        public List<string> GetLoanAccountIds(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
                return new List<string>();

            return _context.LoanAccounts
                           .Where(a => a.LNAccountId.StartsWith(searchTerm))
                           .Select(a => a.LNAccountId)
                           .Take(10)
                           .ToList();
        }



        public bool IsEmployeeOrManager(string id)
        {
            return _context.Employees.Any(e => e.EmployeeId == id)
                || _context.Managers.Any(m => m.ManagerId == id);
        }

 
        public List<Customer> GetEligibleCustomers()
        {
            return _context.Customers
                .OrderBy(c => c.CustomerId)
                .ToList();
        }

        public FixedDeposit CreateFD(FixedDeposit fdInput, string createdById)
        {
          
            if (string.IsNullOrEmpty(fdInput.CustomerId))
                throw new Exception("Customer ID is required.");

            if (!fdInput.Amount.HasValue || fdInput.Amount < 10000)
                throw new Exception("Minimum deposit is ₹10,000.");

            if (!fdInput.OpenDate.HasValue)
                throw new Exception("Open Date is required.");

            if (!fdInput.TenureMonths.HasValue || fdInput.TenureMonths <= 6)
                throw new Exception("Tenure must be at least 7 month.");

            if (!int.TryParse(createdById, out int createdByInt))
                throw new Exception("Invalid creator ID.");

            var customer = _context.Customers.FirstOrDefault(c => c.CustomerId == fdInput.CustomerId);
            if (customer == null)
                throw new Exception("Customer not found.");
            if (fdInput.OpenDate.Value.Date > DateTime.Today)
                throw new Exception("FD Open Date cannot be in the future. Please select today or a past date.");

            // 🎯 Calculate age
            var age = DateTime.Now.Year - customer.CustomerDOB.Value.Year;
            if (customer.CustomerDOB.Value.Date > DateTime.Now.AddYears(-age)) age--;

            // 🎯 Determine ROI
            decimal roi = fdInput.TenureMonths <= 12 ? 6m :
                          fdInput.TenureMonths <= 24 ? 7m : 8m;

            if (age >= 60)
                roi += 0.5m;

            // 🎯 Calculate maturity
            decimal principal = fdInput.Amount.Value;
            int months = fdInput.TenureMonths.Value;
            decimal maturityAmount = principal * (decimal)Math.Pow((double)(1 + (roi / 1200)), months);

            // 🔐 Generate unique FD ID (FD + 5 digits)
            string fdAccountId;
            Random rnd = new Random();
            do
            {
                int number = rnd.Next(10000, 99999);
                fdAccountId = "FD" + number.ToString();
            }
            while (_context.FixedDeposits.Any(f => f.FDAccountId == fdAccountId));

            // 🏦 Create FD record
            var fd = new FixedDeposit
            {
                FDAccountId = fdAccountId,
                CustomerId = fdInput.CustomerId,
                OpenDate = fdInput.OpenDate,
                TenureMonths = fdInput.TenureMonths,
                Amount = fdInput.Amount,
                ROI = roi,
                MaturityAmount = Math.Round(maturityAmount, 2),
                MaturityDate = fdInput.OpenDate?.AddMonths(fdInput.TenureMonths.Value),
                Status = "Active",
                CreatedByEmpId = createdByInt,
                CloseDate = null
            };

            _context.FixedDeposits.Add(fd);

            // 🏦 Create Account record
            var account = new Account
            {
                AccountId = fdAccountId,
                CustomerId = fdInput.CustomerId,
                CreatedBy = createdByInt,
                OpenDate = fdInput.OpenDate,
                AccountStatus = "Active"
            };

            _context.Accounts.Add(account);

            // 💾 Save and catch validation errors
            try
            {
                _context.SaveChanges();
            }
            catch (DbEntityValidationException ex)
            {
                foreach (var validationErrors in ex.EntityValidationErrors)
                {
                    foreach (var error in validationErrors.ValidationErrors)
                    {
                        System.Diagnostics.Debug.WriteLine($"Property: {error.PropertyName}, Error: {error.ErrorMessage}");
                    }
                }

                throw new Exception("Validation failed. Check Output window for details.");
            }

            return fd;
        }

        public string CloseFD(string fdAccountId, string closedById)
        {
            var fd = _context.FixedDeposits.FirstOrDefault(f => f.FDAccountId == fdAccountId);
            if (fd == null) throw new Exception("FD not found.");
            if (fd.Status != "Active") throw new Exception("FD is not active.");
            if (DateTime.Today < fd.MaturityDate) throw new Exception("FD has not matured yet.");

            // Update FD
            fd.Status = "Closed";
            fd.CloseDate = DateTime.Today;

            // Update Account
            var account = _context.Accounts.FirstOrDefault(a => a.AccountId == fdAccountId);
            if (account != null)
            {
                account.AccountStatus = "Closed";
                account.CloseDate = DateTime.Today;
            }

            // Insert FDTransaction
            var transaction = new FDTransaction
            {
                FDAccountId = fdAccountId,
                TransactionDate = DateTime.Today,
                TransactionType = "FD Closure",
                Remark = $"FD closed by {closedById} on maturity",
                Amount = fd.MaturityAmount
            };
            _context.FDTransactions.Add(transaction);

            _context.SaveChanges();
            return $"FD {fdAccountId} closed successfully. Amount credited: ₹{fd.MaturityAmount}";
        }

 

        //public string PrematureWithdrawFD(string fdAccountId, string withdrawnById)
        //{
        //    var fd = _context.FixedDeposits.FirstOrDefault(f => f.FDAccountId == fdAccountId);
        //    if (fd == null) throw new Exception("FD not found.");
        //    if (fd.Status != "Active") throw new Exception("FD is not active.");
        //    if (DateTime.Today >= fd.MaturityDate) throw new Exception("FD already matured. Use Close FD instead.");

        //    // Apply penalty: reduce ROI by 2%
        //    decimal penaltyROI = Math.Max(fd.ROI.Value - 2m, 0);
        //    int months = fd.TenureMonths.Value;
        //    decimal principal = fd.Amount.Value;
        //    decimal prematureAmount = principal * (decimal)Math.Pow((double)(1 + penaltyROI / 100), months / 12.0);

        //    // Update FD
        //    fd.Status = "Premature Withdrawal";
        //    fd.CloseDate = DateTime.Today;
        //    fd.MaturityAmount = Math.Round(prematureAmount, 2);

        //    // Update Account
        //    var account = _context.Accounts.FirstOrDefault(a => a.AccountId == fdAccountId);
        //    if (account != null)
        //    {
        //        account.AccountStatus = "Premature Withdrawal";
        //        account.CloseDate = DateTime.Today;
        //    }

        //    // Insert FDTransaction
        //    var transaction = new FDTransaction
        //    {
        //        FDAccountId = fdAccountId,
        //        TransactionDate = DateTime.Today,
        //        TransactionType = "Premature Withdrawal",
        //        Remark = $"FD closed before maturity by {withdrawnById}",
        //        Amount = fd.MaturityAmount
        //    };
        //    _context.FDTransactions.Add(transaction);

        //    _context.SaveChanges();
        //    return $"FD {fdAccountId} withdrawn prematurely. Final amount: ₹{fd.MaturityAmount}";
        //}



public string PrematureWithdrawFD(string fdAccountId, string withdrawnById)
    {
        var fd = _context.FixedDeposits.FirstOrDefault(f => f.FDAccountId == fdAccountId);
        if (fd == null) throw new Exception("FD not found.");
        if (fd.Status != "Active") throw new Exception("FD is not active.");
        if (DateTime.Today >= fd.MaturityDate) throw new Exception("FD already matured. Use Close FD instead.");

        // Apply penalty: reduce ROI by 2%
        decimal penaltyROI = Math.Max(fd.ROI.GetValueOrDefault() - 2m, 0);
        int months = fd.TenureMonths.GetValueOrDefault();
        decimal principal = fd.Amount.GetValueOrDefault();
        decimal prematureAmount = principal * (decimal)Math.Pow((double)(1 + penaltyROI / 100), months / 12.0);

        // Update FD
        fd.Status = "Premature Withdrawal";
        fd.CloseDate = DateTime.Today;
        fd.MaturityAmount = Math.Round(prematureAmount, 2);

        // Update Account
        var account = _context.Accounts.FirstOrDefault(a => a.AccountId == fdAccountId);
        if (account != null)
        {
            account.AccountStatus = "Premature Withdrawal";
            account.CloseDate = DateTime.Today;
        }

        // Insert FDTransaction
        var transaction = new FDTransaction
        {
            FDAccountId = fdAccountId,
            TransactionDate = DateTime.Today,
            TransactionType = "Premature Withdrawal",
            Remark = $"FD closed before maturity by {withdrawnById}",
            Amount = fd.MaturityAmount
        };
        _context.FDTransactions.Add(transaction);

        try
        {
            _context.SaveChanges();
        }
        catch (DbEntityValidationException ex)
        {
            var sb = new StringBuilder();
            foreach (var failure in ex.EntityValidationErrors)
            {
                sb.AppendLine($"Entity of type \"{failure.Entry.Entity.GetType().Name}\" has the following validation errors:");
                foreach (var error in failure.ValidationErrors)
                {
                    sb.AppendLine($"- Property: {error.PropertyName}, Error: {error.ErrorMessage}");
                }
            }

            // Log or return the error message
            throw new Exception("Entity Validation Failed - errors:\n" + sb.ToString());
        }

        return $"FD {fdAccountId} withdrawn prematurely. Final amount: ₹{fd.MaturityAmount}";
    }


    public List<FixedDeposit> GetFDTransactions(string createdById)
        {
            if (int.TryParse(createdById, out int empId))
            {
                return _context.FixedDeposits
                    .Where(fd => fd.CreatedByEmpId == empId)
                    .OrderByDescending(fd => fd.OpenDate)
                    .ToList();
            }

            return new List<FixedDeposit>(); // or handle invalid input appropriately
        }

        public List<FixedDeposit> GetActiveFDs()
        {
            return _context.FixedDeposits
                .Where(fd => fd.Status == "Active")
                .OrderBy(fd => fd.FDAccountId)
                .ToList();
        }

        // ✅ Get all FDs created by this employee/manager
        public List<FixedDeposit> GetFDsCreatedBy(string id)
        {
            if (int.TryParse(id, out int empId))
            {
                return _context.FixedDeposits
                    .Where(fd => fd.CreatedByEmpId == empId)
                    .OrderByDescending(fd => fd.OpenDate)
                    .ToList();
            }

            return new List<FixedDeposit>(); // or handle invalid input appropriately
        }


        public List<FixedDeposit> GetMaturedFDs()
        {
            return _context.FixedDeposits
                .Where(fd => fd.Status == "Active" && fd.MaturityDate <= DateTime.Today)
                .OrderBy(fd => fd.FDAccountId)
                .ToList();
        }

        public List<FixedDeposit> GetNonMaturedFDs()
        {
            return _context.FixedDeposits
                .Where(fd => fd.Status == "Active" && fd.MaturityDate > DateTime.Today)
                .OrderBy(fd => fd.FDAccountId)
                .ToList();
        }


    }
    public class PendingApprovalViewModel
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public string DepartmentID { get; set; }
        public string PAN { get; set; }
    }
}




