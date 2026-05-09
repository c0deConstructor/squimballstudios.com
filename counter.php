<?php
header('Content-Type: application/json');
header('Access-Control-Allow-Origin: *');

$dataDir  = __DIR__ . '/data';
$dataFile = $dataDir . '/vc.json';

// Prefer real IP; handle Cloudflare / reverse-proxy headers
function clientIP(): string {
    foreach (['HTTP_CF_CONNECTING_IP', 'HTTP_X_FORWARDED_FOR', 'HTTP_X_REAL_IP', 'REMOTE_ADDR'] as $h) {
        if (!empty($_SERVER[$h])) {
            $ip = trim(explode(',', $_SERVER[$h])[0]);
            if (filter_var($ip, FILTER_VALIDATE_IP)) return $ip;
        }
    }
    return '0.0.0.0';
}

function makeToken(): string {
    return bin2hex(random_bytes(16));
}

// Create data dir + block direct HTTP access on first run
if (!is_dir($dataDir)) {
    mkdir($dataDir, 0755, true);
    file_put_contents($dataDir . '/.htaccess', "Deny from all\n");
}

$fp = fopen($dataFile, 'c+');
if (!$fp) { echo json_encode(['n' => 0]); exit; }

flock($fp, LOCK_EX);

$raw = stream_get_contents($fp);
$db  = ($raw !== '') ? json_decode($raw, true) : null;
if (!is_array($db)) $db = ['seq' => 0, 'v' => [], 't' => []];
if (!isset($db['t'])) $db['t'] = []; // migrate older files that lack token table

$clientToken = $_GET['tok'] ?? '';
$ipKey       = hash('sha256', clientIP());
$n           = null;
$retToken    = null;

// 1. Token lookup — returning visitor regardless of IP change
if ($clientToken !== '' && isset($db['t'][$clientToken])) {
    $n        = $db['t'][$clientToken];
    $retToken = $clientToken; // same token, no need to issue a new one
}

// 2. IP hash lookup — same IP, no token yet (or token not found)
if ($n === null && isset($db['v'][$ipKey])) {
    $n = $db['v'][$ipKey];
    // Issue a token now so future IP-change visits still resolve to this number
    $retToken       = makeToken();
    $db['t'][$retToken] = $n;
}

// 3. Brand new visitor
if ($n === null) {
    $n              = ++$db['seq'];
    $db['v'][$ipKey] = $n;
    $retToken        = makeToken();
    $db['t'][$retToken] = $n;
}

rewind($fp);
ftruncate($fp, 0);
fwrite($fp, json_encode($db));
flock($fp, LOCK_UN);
fclose($fp);

echo json_encode(['n' => $n, 'tok' => $retToken]);
