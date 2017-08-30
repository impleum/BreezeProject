﻿using System;
using System.Threading.Tasks;
using Breeze.TumbleBit.Client.Models;
using NBitcoin;
using NTumbleBit.ClassicTumbler;

namespace Breeze.TumbleBit.Client
{
    /// <summary>
    /// An interface for managing interactions with the TumbleBit service.
    /// </summary>
    public interface ITumbleBitManager
    {
        /// <summary>
        /// Connects to the tumbler.
        /// </summary>
        /// <param name="serverAddress">The URI of the tumbler.</param>
        /// <returns></returns>
        Task<ClassicTumblerParameters> ConnectToTumblerAsync(Uri serverAddress);

        Task TumbleAsync(string originWalletName, string destinationWalletName, string originWalletPassword);

        /// <summary>
        /// Returns whether the tumbler is tumbling.
        /// </summary>
        /// <returns></returns>
        Task<bool> IsTumblingAsync();

        /// <summary>
        /// Stops the tumbler if it is tumbling.
        /// </summary>
        /// <returns></returns>
        Task StopAsync();

        /// <summary>
        /// Get the confirmed and unconfirmed balances from the watchonly wallet.
        /// </summary>
        /// <returns></returns>
        Task<WatchOnlyBalances> GetWatchOnlyBalances();

        /// <summary>
        /// Processes a block received from the network.
        /// </summary>
        /// <param name="height">The height of the block in the blockchain.</param>
        /// <param name="block">The block.</param>
        void ProcessBlock(int height, Block block);

        /// <summary>
        /// Pauses the tumbling.
        /// </summary>
        void PauseTumbling();

        /// <summary>
        /// Finishes the tumbling and clean up all saved data.
        /// </summary>
        void FinishTumbling();
    }
}
