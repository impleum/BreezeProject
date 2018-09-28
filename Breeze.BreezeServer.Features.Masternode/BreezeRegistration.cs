using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BreezeCommon;
using NBitcoin;
using NBitcoin.RPC;
using Newtonsoft.Json.Linq;
using NTumbleBit;
using Stratis.Bitcoin.Configuration;

namespace Breeze.BreezeServer.Features.Masternode
{
    public class BreezeRegistration
    {
        // 254 = potentially nonsensical data from internal tests. 253 will be the public testnet version
        // 1 = mainnet protocol version incorporating signature check
        private int PROTOCOL_VERSION_TO_USE = 1;

        public bool CheckBreezeRegistration(NodeSettings nodeSettings, MasternodeSettings masternodeSettings, string regStorePath, string configurationHash, string onionAddress, RsaKey tumblerKey)
        {
            // In order to determine if the registration sequence has been performed
            // before, and to see if a previous performance is still valid, interrogate
            // the database to see if any transactions have been recorded.

            RegistrationStore regStore = new RegistrationStore(regStorePath);

            List<RegistrationRecord> transactions = regStore.GetByServerId(masternodeSettings.TumblerEcdsaKeyAddress);

            // If no transactions exist, the registration definitely needs to be done
            if (transactions == null || transactions.Count == 0) { return false; }

            RegistrationRecord mostRecent = null;

            foreach (RegistrationRecord record in transactions)
            {
                // Find most recent transaction
                if (mostRecent == null)
                {
                    mostRecent = record;
                }

                if (record.RecordTimestamp > mostRecent.RecordTimestamp)
                    mostRecent = record;
            }

            // Check if the stored record matches the current configuration

            RegistrationToken registrationToken;
            try
            {
                registrationToken = mostRecent.Record;
            }
            catch (NullReferenceException e)
            {
                Console.WriteLine(e);
                return false;
            }

            // IPv4
            if (masternodeSettings.Ipv4Address == null && registrationToken.Ipv4Addr != null)
                return false;

            if (masternodeSettings.Ipv4Address != null && registrationToken.Ipv4Addr == null)
                return false;

            if (masternodeSettings.Ipv4Address != null && registrationToken.Ipv4Addr != null)
                if (!masternodeSettings.Ipv4Address.Equals(registrationToken.Ipv4Addr))
                    return false;

            // IPv6
            if (masternodeSettings.Ipv6Address == null && registrationToken.Ipv6Addr != null)
                return false;

            if (masternodeSettings.Ipv6Address != null && registrationToken.Ipv6Addr == null)
                return false;

            if (masternodeSettings.Ipv6Address != null && registrationToken.Ipv6Addr != null)
                if (!masternodeSettings.Ipv6Address.Equals(registrationToken.Ipv6Addr))
                    return false;

            // Onion
            if (onionAddress != registrationToken.OnionAddress)
                return false;

            if (masternodeSettings.TumblerPort != registrationToken.Port)
                return false;

            // This verifies that the tumbler parameters are unchanged
            if (configurationHash != registrationToken.ConfigurationHash)
                return false;
            
            // TODO: Check if transaction is actually confirmed on the blockchain?
            
            return true;
        }

        public Transaction PerformBreezeRegistration(NodeSettings nodeSettings, MasternodeSettings masternodeSettings, string regStorePath, string configurationHash, string onionAddress, RsaKey tumblerKey)
        {
			Network network = Network.StratisMain;
			if (nodeSettings.Network == Network.TestNet || nodeSettings.Network == Network.RegTest ||
			    nodeSettings.Network == Network.StratisTest || nodeSettings.Network == Network.StratisRegTest)
			{
				network = Network.StratisTest;
			}

            //RPCHelper stratisHelper = null;
            RPCClient stratisRpc = null;
            BitcoinSecret privateKeyEcdsa = null;

            try {
                //stratisHelper = new RPCHelper(network);
                //stratisRpc = stratisHelper.GetClient(config.RpcUser, config.RpcPassword, config.RpcUrl);
                privateKeyEcdsa = stratisRpc.DumpPrivKey(BitcoinAddress.Create(masternodeSettings.TumblerEcdsaKeyAddress));
            }
            catch (Exception e) {
                Console.WriteLine("ERROR: Unable to retrieve private key to fund registration transaction");
                Console.WriteLine(e);
                Environment.Exit(0);
            }

            RegistrationToken registrationToken = new RegistrationToken(PROTOCOL_VERSION_TO_USE, masternodeSettings.TumblerEcdsaKeyAddress, masternodeSettings.Ipv4Address, masternodeSettings.Ipv6Address, onionAddress, configurationHash, masternodeSettings.TumblerPort, privateKeyEcdsa.PubKey);
            byte[] msgBytes = registrationToken.GetRegistrationTokenBytes(tumblerKey, privateKeyEcdsa);

            // Create the registration transaction using the bytes generated above
            Transaction rawTx = CreateBreezeRegistrationTx(network, msgBytes, masternodeSettings.TxOutputValueSetting);

            TransactionUtils txUtils = new TransactionUtils();
			RegistrationStore regStore = new RegistrationStore(regStorePath);

            try {
                // Replace fundrawtransaction with C# implementation. The legacy wallet
                // software does not support the RPC call.     
                Transaction fundedTx = txUtils.FundRawTx(stratisRpc, rawTx, masternodeSettings.TxFeeValueSetting, BitcoinAddress.Create(masternodeSettings.TumblerEcdsaKeyAddress));
                RPCResponse signedTx = stratisRpc.SendCommand("signrawtransaction", fundedTx.ToHex());
                Transaction txToSend = new Transaction(((JObject)signedTx.Result)["hex"].Value<string>());

                RegistrationRecord regRecord = new RegistrationRecord(DateTime.Now,
                                                                      Guid.NewGuid(),
                                                                      txToSend.GetHash().ToString(),
                                                                      txToSend.ToHex(),
                                                                      registrationToken,
                                                                      null);

                regStore.Add(regRecord);

                stratisRpc.SendRawTransaction(txToSend);

                return txToSend;
            }
            catch (Exception e)
            {
                Console.WriteLine("ERROR: Unable to broadcast registration transaction");
                Console.WriteLine(e);
            }

            return null;
        }

        public Transaction CreateBreezeRegistrationTx(Network network, byte[] data, Money outputValue)
        {
            // Funding of the transaction is handled by the 'fundrawtransaction' RPC
            // call or its equivalent reimplementation.

            // Only construct the transaction outputs; the change address is handled
            // automatically by the funding logic

            // You need to control *where* the change address output appears inside the
            // transaction to prevent decoding errors with the addresses. Note that if
            // the fundrawtransaction RPC call is used there is an option that can be
            // passed to specify the position of the change output (it is randomly
            // positioned otherwise)

            Transaction sendTx = new Transaction();

            // Recognisable string used to tag the transaction within the blockchain
            byte[] bytes = Encoding.UTF8.GetBytes("BREEZE_REGISTRATION_MARKER");
            sendTx.Outputs.Add(new TxOut()
            {
                Value = outputValue,
                ScriptPubKey = TxNullDataTemplate.Instance.GenerateScriptPubKey(bytes)
            });

            // Add each data-encoding PubKey as a TxOut
            foreach (PubKey pubKey in BlockChainDataConversions.BytesToPubKeys(data))
            {
                TxOut destTxOut = new TxOut()
                {
                    Value = outputValue,
                    ScriptPubKey = pubKey.ScriptPubKey
                };

                sendTx.Outputs.Add(destTxOut);
            }

            if (sendTx.Outputs.Count == 0)
                throw new Exception("ERROR: No outputs in registration transaction, cannot proceed");

            return sendTx;
        }

        public bool VerifyCollateral(NodeSettings nodeSettings, MasternodeSettings masternodeSettings, out Money missingFunds)
        {
            Network network = Network.StratisMain;
            if (nodeSettings.Network == Network.TestNet || nodeSettings.Network == Network.RegTest)
            {
                network = Network.StratisTest;
            }
            /*
            var stratisHelper = new RPCHelper(network);
            var stratisRpc = stratisHelper.GetClient(config.RpcUser, config.RpcPassword, config.RpcUrl);

            RPCResponse listAddressGroupings = stratisRpc.SendCommand("listaddressgroupings");
            var t = listAddressGroupings.Result;

            decimal collateralBalance = (from p in t
                from e in p
                where e.First.Value<String>() == masternodeSettings.TumblerEcdsaKeyAddress
                select e.ElementAt(1).Value<decimal>()).First();

            missingFunds = RegistrationParameters.MASTERNODE_COLLATERAL_THRESHOLD - new Money(collateralBalance, MoneyUnit.BTC);
            if (missingFunds <= 0)
            {
                missingFunds = new Money(0m, MoneyUnit.BTC);
                return true;
            }*/

            missingFunds = 0;
            return true;
        }
    }
}
