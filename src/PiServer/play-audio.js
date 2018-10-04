var player = require('play-sound')(opts = { });

var audio = null;

exports.stop = function (callback) {
	if(audio != null){
		audio.kill();
		audio = null;
	}
	callback(null, "Playback finished");
}

exports.play = function (callback, fileName) {
	audio = player.play(fileName, function (err) {
		if (err && !err.killed) callback(null, err.toString());
		callback(null, "Playback of " + fileName + " finished");
    });
}