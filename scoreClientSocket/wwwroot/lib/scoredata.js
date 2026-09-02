var vUri = "";
var connectionRate;

// Store active event IDs for score updates
var activeEventIDs = [];
var scoreUpdateIntervals = {};
var shortScoreUpdateIntervals = {};

// Constants
var SCORE_UPDATE_INTERVAL = 6000; // 6 seconds
var UPDATE_TYPE = {
    FULL_SCORE: 'fullScore',
    SHORT_SCORE: 'shortScore'
};
var isConnected = false;

function getConnect(vURl, vKey) {
    vUri = vURl;
    // Agent key travels as "?key=" (not a header) — the browser's native WebSocket
    // upgrade request can't carry custom headers. See AgentAuthFilter.
    if (vKey) {
        vUri += (vUri.indexOf('?') === -1 ? '?' : '&') + 'key=' + encodeURIComponent(vKey);
    }
    connectionRate = new signalR.HubConnectionBuilder()
        .withUrl(vUri)
        .build();

    connectionRate.on('Score', function (marketrate) {
        debugger;
        var vjson = marketrate;
        var iControlid = "#txtEventScore" + vjson.eid;
        var iCounterControlid = "#lblcounter" + vjson.eid;
        $(iCounterControlid).html(parseInt($(iCounterControlid).html())+1);
        $(iControlid).val("");
        $(iControlid).val(JSON.stringify(marketrate));
    });
    connectionRate.on('ShortScore', function (marketrate) {
        debugger;
        var vjson = marketrate;
        var iControlid = "#txtEventShortScore" + vjson.eid;
        var iCounterControlid = "#lblcountershortScore" + vjson.eid;
        $(iCounterControlid).html(parseInt($(iCounterControlid).html())+1);
        $(iControlid).val("");
        $(iControlid).val(JSON.stringify(marketrate));
    });

    connectionRate.on('Connected', function (vobj) {
       
        $("#spmsg").html($("#spmsg").html() + " :: " + vobj);
    });

    connectionRate.start()
        .then(function () {
            $("#spmsg").html("connection started....");
            $("#divForm").show();
            $("#signalrConnect :input").attr("disabled", true);
            $("#singalr-submit").fadeOut("slow");
            $("#singalr-submit").fadeOut(3000);
            isConnected = true;
            console.log("SignalR connection established. Ready to subscribe to events.");
        })
        .catch(error => {
            $("#spmsg").html("not connection started....");
            $("#divForm").hide();
            $("#signalrConnect :input").attr("disabled", false);
            $("#singalr-submit").fadein();
            console.error("SignalR connection error: " + error);
        });

    // Handle connection closed
    connectionRate.onclose(function () {
        console.log("SignalR connection closed");
        stopAllScoreUpdates();
       // activeEventIDs = [];
    });

    // Handle reconnection
    // connectionRate.onreconnecting(function () {
    //     console.log("SignalR reconnecting...");
    // });
    /*
    connectionRate.onreconnected(function () {
        console.log("SignalR reconnected. Re-subscribing to active events...");
        // Re-subscribe to active events with single call (comma-separated)
        if (activeEventIDs.length > 0) {
            var eventIDsToResubscribe = activeEventIDs.join(","); // Join all IDs with comma
            console.log("Resubscribing to " + activeEventIDs.length + " events: " + eventIDsToResubscribe);

            connectionRate.invoke("getscore", eventIDsToResubscribe).catch(function (err) {
                console.error("Error resubscribing to events (" + eventIDsToResubscribe + "): " + err.toString());
            });
        }
    });
    */
}
 
/**
 * Connect to score updates for specified event IDs
 * Stores event IDs and initiates automatic score updates every 6 seconds
 */
function scoreConnect() {
    var veventIDsInput = $('#txtEventID').val();

    if (!veventIDsInput || veventIDsInput.trim() === "") {
        console.warn("No event IDs provided for score connection");
        return false;
    }

    var array = veventIDsInput.split(",").map(function (id) {
        return id.trim();
    });

    $.each(array, function (i, veventID) {
        // Skip if already connected
        if (activeEventIDs.indexOf(veventID) !== -1) {
            console.warn("Event ID " + veventID + " already connected");
            return true; // Continue to next
        }

        // Add to active event IDs array
        activeEventIDs.push(veventID);
        console.log("Connected to Event ID: " + veventID);

        // Initial score fetch
        connectionRate.invoke("getscore", veventID).catch(function (err) {
            console.error("Error connecting to score for " + veventID + ": " + err.toString());
        });

        // Create UI element for this event
        var scoreDiv = '<div id="div' + veventID + '" class="score-container" data-event-id="' + veventID + '">' +
            '<div class="score-header">' +
            '<span id="lbl' + veventID + '" class="event-label"><b>Event: ' + veventID + '</b></span>' +
            '&nbsp;:&nbsp;' +
            '<span id="lblcounter' + veventID + '" class="update-counter">0</span> updates' +
            '</div>' +
            '<textarea id="txtEventScore' + veventID + '" placeholder="Score data will appear here..." tabindex="5" readonly></textarea>' +
            '</div>';

        $('#scoreControl').append(scoreDiv);

        // Start automatic score updates every 6 seconds
        startScoreUpdates(veventID, UPDATE_TYPE.FULL_SCORE);
    });

    return false;
}

/**
 * Start automatic score updates for a specific event ID
 * @param {string} veventID - Event ID to update
 * @param {string} updateType - Type of update (fullScore or shortScore)
 */
function startScoreUpdates(veventID, updateType) {
    // Clear existing interval if any
    if (scoreUpdateIntervals[veventID]) {
        clearInterval(scoreUpdateIntervals[veventID]);
    }

    // Set up recurring update
    scoreUpdateIntervals[veventID] = setInterval(function () {
        if (connectionRate && isConnected) {
            connectionRate.invoke("getupdateScore", veventID).catch(function (err) {
                console.error("Error updating score for " + veventID + ": " + err.toString());
            });
        }
    }, SCORE_UPDATE_INTERVAL);

    console.log("Started score updates for Event ID: " + veventID + " (every " + SCORE_UPDATE_INTERVAL + "ms)");
}

/**
 * Stop automatic score updates for a specific event ID
 * @param {string} veventID - Event ID to stop updating
 */
function stopScoreUpdates(veventID) {
    if (scoreUpdateIntervals[veventID]) {
        clearInterval(scoreUpdateIntervals[veventID]);
        delete scoreUpdateIntervals[veventID];
        console.log("Stopped score updates for Event ID: " + veventID);
    }
}

/**
 * Disconnect from score updates for a specific event ID
 * Removes event ID from array and cleans up resources
 */
function scoreDisConnect() {
    var veventID = $('#txtEventID').val();

    if (!veventID || veventID.trim() === "") {
        console.warn("No event ID provided for score disconnection");
        return false;
    }
    removeScoreEventID(veventID);
    return false;
}

/**
 * Remove a specific event ID from the active list and clean up
 * @param {string} veventID - Event ID to remove
 */
function removeScoreEventID(veventID) {
    // Stop automatic updates
    stopScoreUpdates(veventID);

    // Remove from active event IDs array
    var index = activeEventIDs.indexOf(veventID);
    if (index !== -1) {
        activeEventIDs.splice(index, 1);
        console.log("Removed Event ID: " + veventID + " from active list");
    }

    // Call backend to disconnect
    connectionRate.invoke("disconnectscore", veventID).catch(function (err) {
        console.error("Error disconnecting score for " + veventID + ": " + err.toString());
    });

    // Remove UI element
    $("#div" + veventID).fadeOut(300, function () {
        $(this).remove();
    });

    console.log("Disconnected from Event ID: " + veventID);
}

/**
 * Connect to short score updates for specified event IDs
 * Similar to scoreConnect but for short scores
 */
function ShortScoreConnect() {
    var veventIDsInput = $('#txtEventID').val();

    if (!veventIDsInput || veventIDsInput.trim() === "") {
        console.warn("No event IDs provided for short score connection");
        return false;
    }

    var array = veventIDsInput.split(",").map(function (id) {
        return id.trim();
    });

    $.each(array, function (i, veventID) {
        // Skip if already connected to short score
        if (shortScoreUpdateIntervals[veventID]) {
            console.warn("Event ID " + veventID + " already connected to short score");
            return true;
        }

        console.log("Connected to Short Score for Event ID: " + veventID);

        // Initial short score fetch
        connectionRate.invoke("getShortScore", veventID).catch(function (err) {
            console.error("Error connecting to short score for " + veventID + ": " + err.toString());
        });

        // Create UI element for this event
        var shortScoreDiv = '<div id="divshortScore' + veventID + '" class="short-score-container" data-event-id="' + veventID + '">' +
            '<div class="score-header">' +
            '<span id="lblshortScore' + veventID + '" class="event-label"><b>Short Score - Event: ' + veventID + '</b></span>' +
            '&nbsp;:&nbsp;' +
            '<span id="lblcountershortScore' + veventID + '" class="update-counter">0</span> updates' +
            '</div>' +
            '<textarea id="txtEventShortScore' + veventID + '" placeholder="Short score data will appear here..." tabindex="5" readonly></textarea>' +
                        '</div>';

        $('#scoreControl').append(shortScoreDiv);

        // Start automatic short score updates every 6 seconds
       // startShortScoreUpdates(veventID);
    });

    return false;
}

/**
 * Start automatic short score updates for a specific event ID
 * @param {string} veventID - Event ID to update
 */
function startShortScoreUpdates(veventID) {
    // Clear existing interval if any
    if (shortScoreUpdateIntervals[veventID]) {
        clearInterval(shortScoreUpdateIntervals[veventID]);
    }

    // Set up recurring update
    shortScoreUpdateIntervals[veventID] = setInterval(function () {
        if (connectionRate && isConnected) {
            connectionRate.invoke("getShortScore", veventID).catch(function (err) {
                console.error("Error updating short score for " + veventID + ": " + err.toString());
            });
        }
    }, SCORE_UPDATE_INTERVAL);

    console.log("Started short score updates for Event ID: " + veventID + " (every " + SCORE_UPDATE_INTERVAL + "ms)");
}

/**
 * Stop automatic short score updates for a specific event ID
 * @param {string} veventID - Event ID to stop updating
 */
function stopShortScoreUpdates(veventID) {
    if (shortScoreUpdateIntervals[veventID]) {
        clearInterval(shortScoreUpdateIntervals[veventID]);
        delete shortScoreUpdateIntervals[veventID];
        console.log("Stopped short score updates for Event ID: " + veventID);
    }
}

/**
 * Disconnect from short score updates for a specific event ID
 */
function ShortScoreDisConnect() {
    var veventID = $('#txtEventID').val();
    if (!veventID || veventID.trim() === "") {
        console.warn("No event ID provided for short score disconnection");
        return false;
    }
    removeShortScoreEventID(veventID);
    return false;
}

/**
 * Remove a specific event ID from the short score active list and clean up
 * @param {string} veventID - Event ID to remove
 */
function removeShortScoreEventID(veventID) {
    // Stop automatic updates
    stopShortScoreUpdates(veventID);

    // Call backend to disconnect
    connectionRate.invoke("disconnectShortScore", veventID).catch(function (err) {
        console.error("Error disconnecting short score for " + veventID + ": " + err.toString());
    });

    // Remove UI element
    $("#divshortScore" + veventID).fadeOut(300, function () {
        $(this).remove();
    });

    console.log("Disconnected from Short Score - Event ID: " + veventID);
}

/**
 * Get all active event IDs
 * @returns {Array} Array of active event IDs
 */
function getActiveEventIDs() {
    return activeEventIDs;
}

/**
 * Check if an event ID is active
 * @param {string} veventID - Event ID to check
 * @returns {boolean} True if active, false otherwise
 */
function isEventIDActive(veventID) {
    return activeEventIDs.indexOf(veventID) !== -1;
}

/**
 * Get the number of active event IDs
 * @returns {number} Count of active event IDs
 */
function getActiveEventCount() {
    return activeEventIDs.length;
}

/**
 * Stop all active score updates (for cleanup on disconnect)
 */
function stopAllScoreUpdates() {
    // Stop all full score updates
    for (var eventID in scoreUpdateIntervals) {
        if (scoreUpdateIntervals.hasOwnProperty(eventID)) {
            clearInterval(scoreUpdateIntervals[eventID]);
        }
    }
    scoreUpdateIntervals = {};

    // Stop all short score updates
    for (var eventID in shortScoreUpdateIntervals) {
        if (shortScoreUpdateIntervals.hasOwnProperty(eventID)) {
            clearInterval(shortScoreUpdateIntervals[eventID]);
        }
    }
    shortScoreUpdateIntervals = {};

    console.log("Stopped all score updates");
}
