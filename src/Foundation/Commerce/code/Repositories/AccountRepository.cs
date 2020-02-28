//    Copyright 2019 EPAM Systems, Inc.
// 
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
// 
//      http://www.apache.org/licenses/LICENSE-2.0
// 
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.

namespace Wooli.Foundation.Commerce.Repositories
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Web.Security;
    using Connect.Managers;
    using Connect.Models;
    using Context;
    using DependencyInjection;
    using ModelMappers;
    using Models;
    using Models.Account;
    using Models.Checkout;
    using Sitecore.Commerce.Engine.Connect.Entities;
    using Sitecore.Commerce.Entities;
    using Sitecore.Commerce.Entities.Customers;
    using Sitecore.Commerce.Services.Customers;
    using Sitecore.Diagnostics;

    [Service(typeof(IAccountRepositry), Lifetime = Lifetime.Singleton)]
    public class AccountRepository : IAccountRepositry
    {
        #region Constructors

        public AccountRepository(
            IAccountManager accountManager,
            IVisitorContext visitorContext,
            IStorefrontContext storefrontContext,
            IEntityMapper entityMapper)
        {
            Assert.ArgumentNotNull(accountManager, nameof(accountManager));
            this.accountManager = accountManager;

            Assert.ArgumentNotNull(storefrontContext, nameof(storefrontContext));
            this.storefrontContext = storefrontContext;

            Assert.ArgumentNotNull(entityMapper, nameof(entityMapper));
            this.entityMapper = entityMapper;
        }

        #endregion

        #region Fields

        private readonly IAccountManager accountManager;
        private readonly IStorefrontContext storefrontContext;
        private readonly IEntityMapper entityMapper;

        #endregion

        #region Contracts

        public Result<CreateAccountResultModel> CreateAccount(CreateAccountModel createAccountModel)
        {
            Assert.ArgumentNotNull(createAccountModel, nameof(createAccountModel));

            var firstName = createAccountModel.FirstName;
            Assert.ArgumentNotNullOrEmpty(firstName, nameof(firstName));

            var lastName = createAccountModel.LastName;
            Assert.ArgumentNotNullOrEmpty(lastName, nameof(lastName));

            var userName = $"{firstName}{lastName}{Guid.NewGuid():N}";

            var email = createAccountModel.Email;
            Assert.ArgumentNotNullOrEmpty(email, nameof(email));

            var password = createAccountModel.Password;
            Assert.ArgumentNotNull(password, nameof(password));

            var shopName = storefrontContext.ShopName;
            Assert.ArgumentNotNull(shopName, nameof(shopName));

            var result = new Result<CreateAccountResultModel>();

            var validateAccountResult =
                ValidateAccount(new ValidateAccountModel {Email = email});

            if (!validateAccountResult.Success)
            {
                result.SetErrors(validateAccountResult.Errors);
                return result;
            }

            if (validateAccountResult.Success && validateAccountResult.Data.Invalid)
            {
                var message = validateAccountResult.Data.InUse
                    ? "Email is already in use"
                    : "Email is invalid";
                result.SetError(message);
                return result;
            }

            var createUserResult =
                accountManager.CreateUser(userName, email, password, shopName);

            if (!createUserResult.ServiceProviderResult.Success)
            {
                result.SetError("Error is occured during user account creation");
                result.SetResult(new CreateAccountResultModel {Created = false, Message = string.Empty});
                return result;
            }

            var createdCommerceUser = createUserResult.Result;

            var enableUserResult =
                accountManager.EnableUser(createdCommerceUser);

            var user = enableUserResult.Result ?? createdCommerceUser;

            //set user data
            user.FirstName = createAccountModel.FirstName;
            user.LastName = createAccountModel.LastName;

            var updateResult = accountManager.UpdateUser(user);

            result.SetResult(MapToCreateAccountResultDto(true, "Created", updateResult.Result));
            return result;
        }

        /// <summary>
        ///     Returns true, if user exists
        /// </summary>
        /// <param name="validateAccountModel"></param>
        /// <returns></returns>
        public Result<ValidateAccountResultModel> ValidateAccount(ValidateAccountModel validateAccountModel)
        {
            Assert.ArgumentNotNull(validateAccountModel, nameof(validateAccountModel));

            var email = validateAccountModel.Email;
            Assert.ArgumentNotNullOrEmpty(email, nameof(email));

            var result = new Result<ValidateAccountResultModel>();

            // comerce connect don't have separate method for user's email validation
            var emailRegex = new Regex(@"\w+([-+.']\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*");
            if (!emailRegex.Match(email).Success)
            {
                result.SetResult(new ValidateAccountResultModel
                {
                    Email = email,
                    Invalid = true
                });

                return result;
            }

            var getUserManagerResponse = this.accountManager.GetUserByEmail(email);
            var emailAlreadyInUse = getUserManagerResponse.Result != null;

            result.SetResult(new ValidateAccountResultModel
            {
                Email = email,
                Invalid = emailAlreadyInUse,
                InUse = emailAlreadyInUse
            });
            return result;
        }

        public Result<ChangePasswordResultModel> ChangePassword(ChangePasswordModel changePasswordModel)
        {
            Assert.ArgumentNotNull(changePasswordModel, nameof(changePasswordModel));

            var result = new Result<ChangePasswordResultModel>();

            var userName = Membership.GetUserNameByEmail(changePasswordModel.Email);

            if (string.IsNullOrWhiteSpace(userName))
            {
                result.SetError("Can not change password");
                return result;
            }

            if (!Membership.ValidateUser(userName, changePasswordModel.OldPassword))
            {
                result.SetError("Can not change password");
                return result;
            }

            var sitecoreUser = Membership.GetUser(userName);

            if (sitecoreUser == null)
            {
                result.SetError("Can not change password");
                return result;
            }

            var resetedPassword = sitecoreUser.ResetPassword();
            sitecoreUser.ChangePassword(resetedPassword, changePasswordModel.NewPassword);

            result.SetResult(new ChangePasswordResultModel {PasswordChanged = true});
            return result;
        }

        public Result<CommerceUserModel> UpdateAccountInfo(CommerceUserModel user)
        {
            Assert.ArgumentNotNull(user, nameof(user));

            var result = new Result<CommerceUserModel>();

            var getUserResponse =
                accountManager.GetUser(user.ContactId);

            if (!getUserResponse.ServiceProviderResult.Success || getUserResponse.Result == null)
            {
                result.SetErrors(getUserResponse.ServiceProviderResult);
                return result;
            }

            var userForUpdate = getUserResponse.Result;

            userForUpdate.FirstName = user.FirstName;
            userForUpdate.LastName = user.LastName;

            var userUpdateResponse =
                accountManager.UpdateUser(userForUpdate);

            if (!userUpdateResponse.ServiceProviderResult.Success || userUpdateResponse.Result == null)
            {
                result.SetErrors(userUpdateResponse.ServiceProviderResult);
                return result;
            }

            result.SetResult(entityMapper.MapToCommerceUserModel(userUpdateResponse.Result));
            return result;
        }

        public Result<IEnumerable<AddressModel>> AddCustomerAddress(string userName, AddressModel address)
        {
            var result = new Result<IEnumerable<AddressModel>>();

            var getCustomerResult = GetCustomerByUserName(userName);

            if (!getCustomerResult.Success || getCustomerResult.Data == null)
            {
                result.SetErrors(getCustomerResult.Errors);
                return result;
            }

            var partyId = Guid.NewGuid().ToString("N");
            var newParty = new CommerceParty {Name = partyId, ExternalId = partyId, PartyId = partyId};

            UpdateCommerceParty(newParty, address);

            var createPartyResponse =
                accountManager.AddParties(getCustomerResult.Data, new List<Party> {newParty});

            if (!createPartyResponse.ServiceProviderResult.Success)
            {
                result.SetErrors(createPartyResponse.ServiceProviderResult);
                return result;
            }

            return GetAddressList(userName);
        }

        public Result<IEnumerable<AddressModel>> RemoveCustomerAddress(string userName, AddressModel address)
        {
            Assert.ArgumentNotNullOrEmpty(userName, nameof(userName));
            Assert.ArgumentNotNull(address, nameof(address));

            var result = new Result<IEnumerable<AddressModel>>();

            var getCustomerResult = GetCustomerByUserName(userName);

            if (!getCustomerResult.Success || getCustomerResult.Data == null)
            {
                result.SetErrors(getCustomerResult.Errors);
                return result;
            }

            var getPartiesResponse =
                accountManager.GetParties(getCustomerResult.Data);

            if (!getPartiesResponse.ServiceProviderResult.Success || getPartiesResponse.Result == null)
            {
                result.SetErrors(getPartiesResponse.ServiceProviderResult);
                return result;
            }

            var customerParties = getPartiesResponse.Result;
            var partyForRemove = customerParties
                .FirstOrDefault(party => party.ExternalId == address.ExternalId);

            var removePartyResponse = accountManager.RemoveParties(
                getCustomerResult.Data,
                new List<Party> {partyForRemove});

            if (!removePartyResponse.ServiceProviderResult.Success)
            {
                result.SetErrors(removePartyResponse.ServiceProviderResult);
                return result;
            }

            return GetAddressList(userName);
        }

        public Result<IEnumerable<AddressModel>> UpdateAddress(string userName, AddressModel address)
        {
            Assert.ArgumentNotNullOrEmpty(userName, nameof(userName));
            Assert.ArgumentNotNull(address, nameof(address));

            var result = new Result<IEnumerable<AddressModel>>();

            var getCustomerResult = GetCustomerByUserName(userName);

            if (!getCustomerResult.Success || getCustomerResult.Data == null)
            {
                result.SetErrors(getCustomerResult.Errors);
                return result;
            }

            var getPartiesResponse =
                accountManager.GetParties(getCustomerResult.Data);

            if (!getPartiesResponse.ServiceProviderResult.Success || getPartiesResponse.Result == null)
            {
                result.SetErrors(getPartiesResponse.ServiceProviderResult);
                return result;
            }

            var partyForUpdate = getPartiesResponse.Result
                .FirstOrDefault(p => p.ExternalId == address.ExternalId) as CommerceParty;

            if (partyForUpdate == null)
            {
                result.SetError("party not found");
                return result;
            }

            UpdateCommerceParty(partyForUpdate, address);

            var updatePartyResponse =
                accountManager.UpdateParties(
                    getCustomerResult.Data,
                    new List<Party> {partyForUpdate});

            if (!updatePartyResponse.ServiceProviderResult.Success)
            {
                result.SetErrors(updatePartyResponse.ServiceProviderResult);
                return result;
            }

            return GetAddressList(userName);
        }

        public Result<IEnumerable<AddressModel>> GetAddressList(string userName)
        {
            Assert.ArgumentNotNullOrEmpty(userName, nameof(userName));

            var result = new Result<IEnumerable<AddressModel>>();

            var getCustomerResult = GetCustomerByUserName(userName);

            if (!getCustomerResult.Success || getCustomerResult.Data == null)
            {
                result.SetErrors(getCustomerResult.Errors);
                return result;
            }

            var getPartiesResponse =
                accountManager.GetParties(getCustomerResult.Data);

            if (!getPartiesResponse.ServiceProviderResult.Success || getPartiesResponse.Result == null)
            {
                result.SetErrors(getPartiesResponse.ServiceProviderResult);
                return result;
            }

            var customerParties = getPartiesResponse.Result;
            var customerAddresses = customerParties
                .Select(p => entityMapper.MapToAddress(p));

            result.SetResult(customerAddresses);
            return result;
        }

        #endregion

        #region Utils

        private CreateAccountResultModel MapToCreateAccountResultDto(
            bool created,
            string message,
            CommerceUser commerceUser)
        {
            var accountInfo = entityMapper.MapToCommerceUserModel(commerceUser);
            return new CreateAccountResultModel {Created = created, Message = message, AccountInfo = accountInfo};
        }

        private Result<CommerceCustomer> GetCustomerByUserName(string userName)
        {
            Assert.ArgumentNotNullOrEmpty(userName, nameof(userName));

            var result = new Result<CommerceCustomer>();

            var getUserResponse =
                accountManager.GetUser(userName);

            if (!getUserResponse.ServiceProviderResult.Success || getUserResponse.Result == null)
            {
                result.SetErrors(getUserResponse.ServiceProviderResult);
                return result;
            }

            result.SetResult(new CommerceCustomer {ExternalId = getUserResponse.Result.ExternalId});

            return result;
        }

        private void UpdateCommerceParty(CommerceParty partyForUpdate, AddressModel address)
        {
            var countryRegionModel = storefrontContext.CurrentStorefront
                .CountriesRegionsConfiguration.CountriesRegionsModel
                .FirstOrDefault(
                    c => c.CountryCode == address.CountryCode);
            var subdivisionModel =
                countryRegionModel?.Subdivisions.FirstOrDefault(s => s.Code == address.State);

            partyForUpdate.FirstName = address.FirstName;
            partyForUpdate.LastName = address.LastName;
            partyForUpdate.Address1 = address.Address1;
            partyForUpdate.Address2 = address.Address2;
            partyForUpdate.City = address.City;
            partyForUpdate.CountryCode = address.CountryCode;
            partyForUpdate.Country = address.Country;
            partyForUpdate.State = address.State;
            partyForUpdate.RegionName = subdivisionModel?.Name;
            partyForUpdate.RegionCode = address.State;
            partyForUpdate.ZipPostalCode = address.ZipPostalCode;
        }

        #endregion
    }
}