﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.RPC;
using Newtonsoft.Json.Linq;

namespace NTumbleBit.TumblerServer.Services.RPCServices
{
	public class RPCWalletService : IWalletService
	{
		public RPCWalletService(RPCClient rpc)
		{
			if(rpc == null)
				throw new ArgumentNullException("rpc");
			_RPCClient = rpc;
		}

		private readonly RPCClient _RPCClient;
		public RPCClient RPCClient
		{
			get
			{
				return _RPCClient;
			}
		}

		public IDestination GenerateAddress()
		{
			return _RPCClient.GetNewAddress();
		}

		public Key GenerateNewKey()
		{
			var address = _RPCClient.GetNewAddress();
			return _RPCClient.DumpPrivKey(address).PrivateKey;
		}

		public Coin AsCoin(UnspentCoin c)
		{
			var coin = new Coin(c.OutPoint, new TxOut(c.Amount, c.ScriptPubKey));
			if(c.RedeemScript != null)
				coin = coin.ToScriptCoin(c.RedeemScript);
			return coin;
		}

		public Transaction FundTransaction(TxOut txOut, FeeRate feeRate)
		{
			Transaction tx = new Transaction();
			tx.Outputs.Add(txOut);
			var result = _RPCClient.SendCommand("fundrawtransaction", tx.ToHex(), new JObject()
			{
				new JProperty("lockUnspents", true),
				new JProperty("feeRate", feeRate.GetFee(1024).ToDecimal(MoneyUnit.BTC)),				
			});			
			if(result.Error != null)
				return null;
			var jobj = (JObject)result.Result;
			var hex = jobj["hex"].Value<string>();
			tx = new Transaction(hex);
			result = _RPCClient.SendCommand("signrawtransaction", tx.ToHex());
			if(result.Error != null)
				return null;
			jobj = (JObject)result.Result;
			hex = jobj["hex"].Value<string>();
			return new Transaction(hex);
		}
	}
}
