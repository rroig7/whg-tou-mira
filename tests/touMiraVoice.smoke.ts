import net from 'net';
import assert from 'assert';
import { touMiraBridge } from '../BetterCrewlink/src/main/touMiraBridge';
import { TOU_MIRA_BRIDGE_PORT, TouMiraPlayer, TouMiraSnapshot } from '../BetterCrewlink/src/common/TouMira';
import { AmongUsState, GameState, Player } from '../BetterCrewlink/src/common/AmongUsState';
import { MapType, CameraLocation } from '../BetterCrewlink/src/common/AmongusMap';
import { defaultLobbySettings } from '../BetterCrewlink/src/renderer/voice/types';
import { calculateVoiceAudio } from '../BetterCrewlink/src/renderer/voice/spatialAudio';
import { touMiraMuteReason } from '../BetterCrewlink/src/renderer/voice/touMiraVoice';

const tp = (id: number, extra: Partial<TouMiraPlayer> = {}): TouMiraPlayer => ({
	id,
	dead: false,
	blackmailed: false,
	blackmailedNextRound: false,
	jailed: false,
	jailorAlive: false,
	puppeteerControllerId: -1,
	parasiteControllerId: -1,
	swooped: false,
	hacked: false,
	blinded: false,
	hysteria: false,
	...extra,
});
const player = (id: number, x: number, y: number, extra: Partial<Player> = {}): Player =>
	({
		id,
		clientId: id + 100,
		x,
		y,
		isDead: false,
		isImpostor: false,
		disconnected: false,
		isDummy: false,
		inVent: false,
		isLocal: false,
		...extra,
	}) as Player;

function state(gameState: GameState, players: Player[], tou: TouMiraSnapshot | null): AmongUsState {
	return {
		gameState,
		players,
		touMira: tou,
		map: MapType.THE_SKELD,
		currentCamera: CameraLocation.NONE,
		closedDoors: [],
		comsSabotaged: false,
		lightRadiusChanged: false,
	} as unknown as AmongUsState;
}
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const settings = { enableSpatialAudio: true, ghostVolumeAsImpostor: 100 } as any;
const lobby = { ...defaultLobbySettings };
const gain = (s: AmongUsState, me: Player, other: Player, l = lobby) =>
	calculateVoiceAudio({
		state: s,
		settings,
		activeLobbySettings: l,
		me,
		other,
		maxDistance: 5,
		impostorRadioClientId: -1,
		inGracePeriod: false,
	});
const snapOf = (phase: TouMiraSnapshot['phase'], players: TouMiraPlayer[]): TouMiraSnapshot => ({
	v: 1,
	phase,
	localId: 0,
	players,
});

async function main() {
	// transport: garbage line, then a snapshot split across two TCP writes
	const snap = snapOf('meeting', [tp(0), tp(1, { jailed: true, jailorAlive: true })]);
	const server = net.createServer((sock) => {
		const line = JSON.stringify(snap) + '\n';
		sock.write('not json\n' + line.slice(0, 20));
		setTimeout(() => sock.write(line.slice(20)), 50);
	});
	await new Promise<void>((r) => server.listen(TOU_MIRA_BRIDGE_PORT, '127.0.0.1', r));
	touMiraBridge.start();
	await new Promise((r) => setTimeout(r, 400));
	assert.deepStrictEqual(touMiraBridge.snapshot(), snap, 'client parses split NDJSON line');
	touMiraBridge.stop();
	server.close();
	console.log('ok  transport: reassembles split lines, skips garbage');

	const me = player(0, 0, 0, { isLocal: true });
	const other = player(1, 1, 0);

	// jailor
	const jailMeeting = state(GameState.DISCUSSION, [me, other], snap);
	assert.strictEqual(gain(jailMeeting, me, other).gain, 0);
	assert.strictEqual(touMiraMuteReason(jailMeeting, lobby, other), 'jailed');
	assert.strictEqual(gain(jailMeeting, me, other, { ...lobby, touMira_muteJailedInMeetings: false }).gain, 1);
	assert.strictEqual(gain(jailMeeting, me, other, { ...lobby, touMira_enabled: false }).gain, 1);
	const deadJailor = state(
		GameState.DISCUSSION,
		[me, other],
		snapOf('meeting', [tp(0), tp(1, { jailed: true, jailorAlive: false })])
	);
	assert.strictEqual(gain(deadJailor, me, other).gain, 1);
	assert.strictEqual(gain(deadJailor, me, other, { ...lobby, touMira_jailPersistsAfterJailorDeath: true }).gain, 0);
	assert.strictEqual(gain(state(GameState.TASKS, [me, other], { ...snap, phase: 'tasks' }), me, other).gain, 1);
	console.log('ok  jailor: meeting-only mute, jailor-death + persist option, master switch');

	assert.strictEqual(gain(state(GameState.DISCUSSION, [me, other], null), me, other).gain, 1);
	console.log('ok  no bridge: vanilla behaviour');

	// blackmailer
	const bm = (phase: TouMiraSnapshot['phase'], gs: GameState, p: Partial<TouMiraPlayer>) =>
		state(gs, [me, other], snapOf(phase, [tp(0), tp(1, p)]));
	assert.strictEqual(gain(bm('meeting', GameState.DISCUSSION, { blackmailed: true }), me, other).gain, 0);
	assert.strictEqual(gain(bm('exile', GameState.TASKS, { blackmailed: true }), me, other).gain, 0, 'exile');
	assert.strictEqual(gain(bm('tasks', GameState.TASKS, { blackmailed: true }), me, other).gain, 1, 'pre-meeting');
	assert.strictEqual(gain(bm('tasks', GameState.TASKS, { blackmailedNextRound: true }), me, other).gain, 1);
	assert.strictEqual(
		gain(bm('tasks', GameState.TASKS, { blackmailedNextRound: true }), me, other, {
			...lobby,
			touMira_muteBlackmailedNextRound: true,
		}).gain,
		0
	);
	console.log('ok  blackmailer: meeting + exile mute, optional next-round mute');

	// puppeteer
	const puppeteer = player(0, 0, 0, { isLocal: true, isImpostor: true });
	const victim = player(1, 20, 0);
	const nearVictim = player(2, 21, 0);
	const ctrl = state(
		GameState.TASKS,
		[puppeteer, victim, nearVictim],
		snapOf('tasks', [tp(0), tp(1, { puppeteerControllerId: 0 }), tp(2)])
	);
	assert.strictEqual(gain(ctrl, puppeteer, victim).gain, 0, 'victim muted');
	const heard = gain(ctrl, puppeteer, nearVictim);
	assert.strictEqual(heard.gain, 1, 'puppeteer hears near victim');
	assert.deepStrictEqual(heard.panPosition, [1, 0], 'panned relative to victim');
	assert.strictEqual(
		gain(ctrl, puppeteer, nearVictim, { ...lobby, touMira_puppeteerHearFromVictim: false }).gain,
		0,
		'out of range without option'
	);
	console.log('ok  puppeteer: victim muted, controller hears from victim position');

	// parasite (additive)
	const nearMe = player(3, 1, 0);
	const para = state(
		GameState.TASKS,
		[puppeteer, victim, nearVictim, nearMe],
		snapOf('tasks', [tp(0), tp(1, { parasiteControllerId: 0 }), tp(2), tp(3)])
	);
	assert.strictEqual(gain(para, puppeteer, nearVictim).gain, 1);
	assert.strictEqual(gain(para, puppeteer, nearMe).gain, 1);
	console.log('ok  parasite: hears around itself and around victim');

	// listener muffle
	const blind = state(GameState.TASKS, [me, other], snapOf('tasks', [tp(0, { blinded: true }), tp(1)]));
	const muffled = gain(blind, me, other);
	assert.ok(muffled.muffle && muffled.muffle.type === 'lowpass' && muffled.gain > 1, 'muffled and boosted');
	assert.strictEqual(gain(blind, me, other, { ...lobby, touMira_muffleBlinded: false }).muffle, false);
	console.log('ok  eclipsal/grenadier: listener muffled');

	// jail deafen: the jailee hears nobody in meetings, only with the option on and a valid jail
	const jailee = state(
		GameState.DISCUSSION,
		[me, other],
		snapOf('meeting', [tp(0, { jailed: true, jailorAlive: true }), tp(1)])
	);
	const deafenOn = { ...lobby, touMira_deafenJailedInMeetings: true };
	assert.strictEqual(gain(jailee, me, other).gain, 1, 'deafen is off by default');
	assert.strictEqual(gain(jailee, me, other, deafenOn).gain, 0);
	const jaileeDeadJailor = state(
		GameState.DISCUSSION,
		[me, other],
		snapOf('meeting', [tp(0, { jailed: true, jailorAlive: false }), tp(1)])
	);
	assert.strictEqual(gain(jaileeDeadJailor, me, other, deafenOn).gain, 1);
	assert.strictEqual(
		gain(jaileeDeadJailor, me, other, { ...deafenOn, touMira_jailPersistsAfterJailorDeath: true }).gain,
		0
	);
	console.log('ok  jailor: optional deafen in meetings');

	// medium: spirit at (20,0); default mode is Ghost -> Medium
	const medium = player(0, 0, 0, { isLocal: true });
	const nearSpiritAlive = player(1, 21, 0);
	const nearBodyAlive = player(2, 1, 0);
	const mediatedGhost = player(3, 21, 0, { isDead: true });
	const otherGhost = player(4, 21, 0, { isDead: true });
	const med = state(
		GameState.TASKS,
		[medium, nearSpiritAlive, nearBodyAlive, mediatedGhost, otherGhost],
		snapOf('tasks', [
			tp(0, { mediumSpirit: [20, 0] }),
			tp(1),
			tp(2),
			tp(3, { dead: true, mediatedBy: 0 }),
			tp(4, { dead: true }),
		])
	);
	assert.strictEqual(gain(med, medium, nearSpiritAlive).gain, 1, 'medium listens from the spirit');
	assert.strictEqual(gain(med, medium, nearBodyAlive).gain, 0, 'not from its body');
	assert.strictEqual(gain(med, medium, mediatedGhost).gain, 1, 'hears its mediated ghost');
	assert.strictEqual(gain(med, medium, otherGhost).gain, 0, 'but not other ghosts');
	const noMedium = { ...lobby, touMira_mediumGhostVoice: 0 };
	assert.strictEqual(gain(med, medium, mediatedGhost, noMedium).gain, 0, 'None: no ghost voice');
	assert.strictEqual(gain(med, medium, nearBodyAlive, noMedium).gain, 1, 'None: hears from its body');
	// the other direction: who hears the Medium while it's in spirit form
	assert.strictEqual(gain(med, nearBodyAlive, medium).gain, 0, 'living players cannot hear the Medium');
	assert.strictEqual(gain(med, mediatedGhost, medium).gain, 0, 'ghosts only with Medium -> Ghost');
	const both = gain(med, mediatedGhost, medium, { ...lobby, touMira_mediumGhostVoice: 3 });
	assert.strictEqual(both.gain, 1, 'Both: ghosts hear the Medium');
	assert.deepStrictEqual(both.panPosition, [-1, 0], 'from the spirit');
	console.log('ok  medium: spirit-form ghost voice in every mode');

	assert.strictEqual(touMiraMuteReason(bm('tasks', GameState.TASKS, { swooped: true }), lobby, other), 'swooped');
	assert.strictEqual(touMiraMuteReason(bm('tasks', GameState.TASKS, { hacked: true }), lobby, other), 'hacked');
	assert.strictEqual(
		touMiraMuteReason(bm('meeting', GameState.DISCUSSION, { jailed: true, jailorAlive: true, dead: true }), lobby, other),
		null
	);
	console.log('ok  swooper/glitch muted; dead players never TOU-muted');
}

main().then(
	() => console.log('ALL PASSED'),
	(e) => {
		console.error(e);
		process.exit(1);
	}
);
