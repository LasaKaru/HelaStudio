package dev.shellwright.shell.net

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.distinctUntilChanged

/** What the device's connection currently looks like. */
public enum class NetworkState {
    /** A validated connection with actual internet access. */
    Online,

    /** No usable connection. */
    Offline,
}

/**
 * Observes connectivity as a cold [Flow].
 *
 * ⚠️ Uses `NET_CAPABILITY_VALIDATED`, not merely "a network exists". A captive
 * portal on hotel or airport wifi presents a connected network that cannot
 * reach anything, and treating that as online is how an app ends up showing a
 * blank screen instead of the offline page.
 */
public class ConnectivityObserver(context: Context) {

    private val manager =
        context.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager

    /** Emits on every change, starting with the state at collection time. */
    public fun observe(): Flow<NetworkState> = callbackFlow {
        trySend(current())

        val callback = object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) {
                trySend(current())
            }

            override fun onLost(network: Network) {
                trySend(current())
            }

            override fun onCapabilitiesChanged(
                network: Network,
                capabilities: NetworkCapabilities,
            ) {
                trySend(current())
            }
        }

        val request = NetworkRequest.Builder()
            .addCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
            .build()

        manager.registerNetworkCallback(request, callback)
        awaitClose { manager.unregisterNetworkCallback(callback) }
    }.distinctUntilChanged()

    /** The state right now. */
    public fun current(): NetworkState {
        val capabilities = manager.getNetworkCapabilities(manager.activeNetwork)
            ?: return NetworkState.Offline

        val usable = capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET) &&
            capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED)

        return if (usable) NetworkState.Online else NetworkState.Offline
    }
}
