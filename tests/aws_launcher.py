"""Execute the real PowerShell launcher with fake aws/dotnet commands: no AWS calls.
Set PWSH_BIN if pwsh isn't on PATH. Tests routing, configuration and stale-port protection.
"""
import json
import os
from pathlib import Path
import shutil
import socket
import subprocess
import tempfile
import unittest
from integration import ROOT

STUB = '''#!/usr/bin/env python3
import json,os,sys
from pathlib import Path
name=Path(sys.argv[0]).name
if name=='aws':
 if sys.argv[1]=='sts': print('arn:aws:iam::123456789012:user/test-only')
 else: print(json.dumps({'KeySchema':[{'AttributeName':'PK','KeyType':'HASH'},{'AttributeName':'SK','KeyType':'RANGE'}],'AttributeDefinitions':[{'AttributeName':'PK','AttributeType':'S'},{'AttributeName':'SK','AttributeType':'S'}],'TableStatus':'ACTIVE'}))
else:
 with open(os.environ['LAUNCH_LOG'],'a') as f: f.write(json.dumps({'args':sys.argv[1:],'provider':os.environ.get('Storage__Provider'),'table':os.environ.get('DynamoDb__TableName'),'region':os.environ.get('DynamoDb__Region'),'endpoint':os.environ.get('DynamoDb__ServiceUrl'),'keys':os.environ.get('Storage__KeysPath')})+'\\n')
 if sys.argv[1]=='build' and os.environ.get('FAIL_BUILD')=='1': sys.exit(1)
'''

class LauncherTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='suportehub-ps-launcher-')
        self.directory=Path(self.temp.name); self.log=self.directory/'calls.jsonl'
        for name in ['aws','dotnet']:
            file=self.directory/name;file.write_text(STUB);file.chmod(0o755)
        self.pwsh=os.environ.get('PWSH_BIN') or shutil.which('pwsh')
        if not self.pwsh: raise RuntimeError('Install PowerShell or set PWSH_BIN to execute these tests.')
        self.env={**os.environ,'PATH':str(self.directory)+os.pathsep+os.environ['PATH'],'LAUNCH_LOG':str(self.log),
                  'XDG_CACHE_HOME':str(self.directory/'cache'),'XDG_CONFIG_HOME':str(self.directory/'config'),
                  'XDG_DATA_HOME':str(self.directory/'data'),'Storage__Provider':'Sqlite',
                  'DynamoDb__TableName':'wrong-table','DynamoDb__Region':'us-east-1','DynamoDb__ServiceUrl':'http://127.0.0.1:1'}

    def tearDown(self): self.temp.cleanup()
    def run_script(self,*args):
        return subprocess.run([self.pwsh,'-NoProfile','-NonInteractive','-File',str(ROOT/'scripts/start-aws.ps1'),*args],
                              env=self.env,cwd=self.directory,text=True,capture_output=True,timeout=25)
    def calls(self): return [json.loads(line) for line in self.log.read_text().splitlines()] if self.log.exists() else []

    def test_01_checkonly_never_builds_or_writes(self):
        result=self.run_script('-CheckOnly');self.assertEqual(result.returncode,0,result.stderr);self.assertEqual(self.calls(),[])

    def test_02_actions_use_same_explicit_aws_configuration(self):
        for switch,flag in [('-CreateAdmin','--create-admin'),('-PromoteAdmin','--promote-admin'),('-CheckAccount','--check-account'),(None,None)]:
            with self.subTest(action=switch):
                if self.log.exists(): self.log.unlink()
                result=self.run_script(*([switch] if switch else []));self.assertEqual(result.returncode,0,result.stderr)
                calls=self.calls();self.assertEqual(len(calls),2)
                self.assertEqual(calls[0]['args'][0],'build');actual=calls[1]
                self.assertTrue(actual['args'][0].endswith('SuporteHub.API.dll'))
                self.assertEqual(actual['args'][1:],['--require-aws=true']+([flag] if flag else []))
                self.assertEqual((actual['provider'],actual['table'],actual['region'],actual['endpoint']),('DynamoDb','suportehub-dev','sa-east-1',''))
                self.assertTrue(Path(actual['keys']).is_absolute())

    def test_03_port_conflict_stops_before_account_or_server_action(self):
        with socket.socket() as listener:
            listener.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1);listener.bind(('127.0.0.1',5218));listener.listen()
            result=self.run_script('-PromoteAdmin')
        self.assertNotEqual(result.returncode,0);self.assertIn('porta 5218',result.stderr);self.assertEqual(self.calls(),[])

    def test_04_build_failure_and_conflicting_actions_do_not_provision(self):
        self.env['FAIL_BUILD']='1';result=self.run_script('-CreateAdmin')
        self.assertNotEqual(result.returncode,0);self.assertEqual(len(self.calls()),1)
        self.log.unlink();result=self.run_script('-CreateAdmin','-PromoteAdmin')
        self.assertNotEqual(result.returncode,0);self.assertEqual(self.calls(),[])

if __name__=='__main__': unittest.main(verbosity=2)
