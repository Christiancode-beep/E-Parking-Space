using Azure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using PayStack.Net;
using SmartParkingSystem.Contracts;
using SmartParkingSystem.Entities.DataTransferObjects;
using SmartParkingSystem.Entities.Models;
using System;
using System.Net.Http.Headers;
using System.Numerics;
using System.Transactions;
using Xamarin.Essentials;

namespace SmartParkingSystem.Repository
{
    public class PaymentRepository : IPaymentRepository
    {
        private readonly RepositoryContext _context;
        private readonly INotificationRepository _notificationRepository;
        private readonly ILogger<PaymentRepository> _logger;
        private readonly IConfiguration _config;
        private PayStackApi _payStackApi;
        private readonly string _secretKey;
        public PaymentRepository(RepositoryContext context, INotificationRepository notificationRepository, IConfiguration config, ILogger<PaymentRepository> logger)
        {
            _config = config;
            _logger = logger;
            _secretKey = _config["Paystack:SecretKey"];
            _payStackApi = new PayStackApi(_secretKey);
            _context = context;
            _notificationRepository = notificationRepository;

        }


        public async Task<InitiateResponse> InitializePayment(PaystackInitateRequest request)
        {

            try
            {
                InitiateResponse response = new InitiateResponse();
                TransactionInitializeRequest paystackRequest = new TransactionInitializeRequest
                {
                    AmountInKobo = int.Parse(request.Amount) * 100,
                    Email = request.Email,
                    Currency = "NGN",
                    CallbackUrl = _config["Paystack:CallBackUrl"]
                };
                _logger.LogInformation($"Request:: {JsonConvert.SerializeObject(paystackRequest)}");
                TransactionInitializeResponse responseFromPaystack = _payStackApi.Transactions.Initialize(paystackRequest);
                _logger.LogInformation($"Response from Paystack:: {JsonConvert.SerializeObject(responseFromPaystack)}");
                if (responseFromPaystack.Status)
                {
                    Payment payment = new Payment
                    {
                        Amount = decimal.Parse(request.Amount),
                        Email = request.Email,
                        Currency = "NGN",
                        Status = "pending",
                        Reference = responseFromPaystack.Data.Reference
                    };
                    //await _context.AddAsync(payment);
                    //await _context.SaveChangesAsync();
                }
                response = new InitiateResponse
                {
                    Status = responseFromPaystack.Status,
                    Message = responseFromPaystack.Message,
                    AuthorizationUrl = responseFromPaystack.Data.AuthorizationUrl,
                    AccessCode = responseFromPaystack.Data.AccessCode,
                    Reference = responseFromPaystack.Data.Reference
                };
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError("An error occured in initialize payment", ex.Message);
                var response = new InitiateResponse
                {
                    Status = false,
                    Message = "An error occured initializing payment"
                };
                return response;
            }
        }
        public async Task<PaystackVerifyResponse> VerifyPayment(string reference)
        {
            try
            {
                PaystackVerifyResponse response = new PaystackVerifyResponse();
                _logger.LogInformation($"Reference:: {reference}");
                TransactionVerifyResponse verifyResponse = _payStackApi.Transactions.Verify(reference);
                if (verifyResponse.Status)
                {
                    _logger.LogInformation($"Response From Paystack:: {JsonConvert.SerializeObject(verifyResponse)}");

                    var updateDatabase = new Payment
                    {
                        Status = "success",
                        Message = verifyResponse.Message,
                        PaymentMethod = verifyResponse.Data.Channel,
                        Banks = verifyResponse.Data.Authorization.Bank,
                        PaymentDate = verifyResponse.Data.TransactionDate
                    };
                    _context.Update(updateDatabase);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    _logger.LogInformation($"Response From Paystack:: {JsonConvert.SerializeObject(verifyResponse)}");
                    var updateDatabase = new Payment
                    {
                        Status = "failed",
                        Message = verifyResponse.Message,
                        Reference = verifyResponse.Data.Reference,
                        PaymentMethod = verifyResponse.Data.Channel,
                        Banks = verifyResponse.Data.Authorization.Bank,
                        PaymentDate = verifyResponse.Data.TransactionDate
                    };
                    _context.Update(updateDatabase);
                    await _context.SaveChangesAsync();
                }
                response = new PaystackVerifyResponse
                {
                    Status = verifyResponse.Status,
                    Message = verifyResponse.Message,
                };
                return response;

            }
            catch (Exception ex)
            {
                _logger.LogError("An error occured while verifying payment", ex.Message);
                var response = new PaystackVerifyResponse
                {
                    Status = false,
                    Message = "Error occured while verifying payment"
                };
                return response;
            }

        }
    }
}
