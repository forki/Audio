var StreamPlayer = require('stream-player');
 
var streamplayer = new StreamPlayer();

exports.play = function (callback, fileName) {
 
	// Add a song url to the queue
	streamplayer.add(fileName);
	
	
	streamplayer.on('play end', function() {
		callback(null, "Playback of " + fileName + " finished");
	});
	
	// Start playing all songs added to the queue (FIFO)
	streamplayer.play();
}

exports.stop = function (callback) {
    streamplayer.pause();
	player = new StreamPlayer();
	
	callback(null, "Playback finished");
}