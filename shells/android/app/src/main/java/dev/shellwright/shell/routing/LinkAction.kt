package dev.shellwright.shell.routing

/** Where a link opens. */
public sealed interface LinkAction {
    /** Load in the current web view. */
    public data object Internal : LinkAction

    /** Load in a modal web view stacked over the current one. */
    public data object Modal : LinkAction

    /** Load in a modal with reader styling applied. */
    public data object ReaderModal : LinkAction

    /** Hand to Chrome Custom Tabs. */
    public data object ExternalBrowser : LinkAction

    /** Refuse the navigation entirely. */
    public data object Block : LinkAction

    /** Hand to another app: `mailto:`, `tel:`, `sms:`, `intent://`. */
    public data class External(val uri: String) : LinkAction

    /** Hand to the download manager. */
    public data class Download(val url: String) : LinkAction
}
