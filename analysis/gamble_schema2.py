"""Independent schema-2 journal checks, categorical replay and signed-ledger exports."""
import json
import math
import re

REPRESENTATIONS={'Bar','Pie','HorizontalBar','Numeric','Grid','Donut'}
OUTCOME_COLORS=('#649ECC','#C48B55','#77AA77','#AD85C4','#EEC75E','#DB8398')

def validate_v2(r, prepared, events, model, issues, trials, options, outcomes, ledger_rows, balances, adaptive_states):
    from analyze_gamble_choice import replay_draw, validate_visual_config
    key=r['TrialId']; c=r['Config']; offered=r['Options']; chosen=r['ChosenIndex']; amount=r.get('Tokens')
    validate_visual_config(c,2)
    def check(ok, message):
        if not ok: issues.append(f'{key}: {message}')
    def integer(x): return type(x) is int
    def number(x): return type(x) in (int,float) and math.isfinite(x)
    check(type(r['SchemaVersion']) is int and type(c['SchemaVersion']) is int and c['SchemaVersion']==2, 'config schema mismatch')
    check(integer(c['ChoiceCount']) and integer(c['Seed']) and -2**31<=c['Seed']<2**31, 'invalid config integer')
    check(isinstance(r.get('ConfigHash'),str) and re.fullmatch('[0-9a-f]{64}',r['ConfigHash']) is not None, 'invalid config hash')
    input_source=r.get('InputSource')
    check(isinstance(input_source,str) and bool(input_source), 'missing input source')
    if not isinstance(input_source,str): input_source='Unknown'
    check(r['Status'] in {'Completed','Omission','Aborted','Stopped'}, 'invalid final status')
    check(r['RandomAlgorithm']=='xorshift32-v1' and r['SamplerAlgorithm']=='categorical-cdf-v1', 'unsupported sampler')
    check(integer(r['TrialIndex']) and r['TrialIndex']>=0 and integer(chosen), 'invalid indices')
    check(integer(r['BlockIndex']) and integer(r['PlanTrialIndex']) and r['PlanTrialIndex']>=0, 'invalid block/plan index')
    check(integer(r['OutcomeIndex']) and (amount is None or integer(amount)), 'invalid outcome index/token type')
    for name in ('FirstContactRTMs','CommitRTMs'):
        if r.get(name) is not None and (not number(r[name]) or r[name]<0):
            raise ValueError(f'invalid {name}')
    if r.get('FirstContactRTMs') is not None and r.get('CommitRTMs') is not None:
        check(r['FirstContactRTMs']<=r['CommitRTMs'], 'contact occurs after commit')
    check(type(r['Practice']) is bool and isinstance(r['ConditionId'],str) and bool(r['ConditionId']), 'missing condition/practice')
    check(isinstance(offered,list) and 2<=len(offered)<=6 and len(offered)==c['ChoiceCount'], 'invalid choice count')
    check(len({o['Id'] for o in offered})==len(offered), 'duplicate choice IDs')
    if not integer(r['RequestedPulses']) or r['RequestedPulses']!=0:
        raise ValueError('virtual ledger requested pulses must be integer zero')
    check(c['PhysicalReward'] is False and r.get('ObservedPulses') is None, 'virtual ledger contains physical reward')
    check(prepared is not None or model, 'missing prepared record')
    if prepared:
        check(all(prepared.get(n)==r.get(n) for n in ('Config','ConfigHash','Options','OnsetMs','TrialIndex','BlockIndex','PlanTrialIndex','Practice','ConditionId','BalanceBefore','AdaptiveBefore')), 'prepared/final configuration changed')
    for name in ('OnsetMs','EndMs','RevealActualMs','TransferEndMs','ItiStartMs','RevealLateMs'):
        if not number(r[name]): raise ValueError(f'invalid {name}')
    check(r['EndMs']>=r['OnsetMs'], 'negative duration')
    for name in ('HoldMs','DeadlineMs','RevealMs','HighlightMs','TransferMs','RewardWindowMs','ItiMs'):
        if not number(c[name]) or c[name]<0: raise ValueError(f'invalid {name}')
    check(0<c['HoldMs']<=c['DeadlineMs']<=c['RevealMs'] and c['HighlightMs']>0 and c['TransferMs']>0, 'invalid timing policy')
    check(integer(c['GridCells']) and 1<=c['GridCells']<=100, 'invalid grid denominator')
    for pos,o in enumerate(offered):
        xs=o['Outcomes']; check(isinstance(xs,list) and 1<=len(xs)<=6, 'invalid outcome count')
        check(len({x['Id'] for x in xs})==len(xs), 'duplicate outcome IDs')
        check(o['ProbabilityRepresentation'] in REPRESENTATIONS and o['RewardRepresentation'] in {'TokenSlots','Numeric'}, 'invalid representation')
        check(integer(o['Palette']) and o['Palette'] in (0,1), 'invalid palette')
        for outcome_index,x in enumerate(xs):
            if not number(x['Probability']) or not 0<=x['Probability']<=1 or not integer(x['TokenDelta']) or not -6<=x['TokenDelta']<=6: raise ValueError('invalid signed outcome')
            check(isinstance(x['Id'],str) and bool(x['Id']), 'missing outcome identity')
            color=x.get('Color')
            check(color is None or isinstance(color,str) and re.fullmatch(r'#[0-9a-fA-F]{6}',color) is not None, 'invalid outcome color')
            if o['ProbabilityRepresentation']=='Grid': check(abs(x['Probability']*c['GridCells']-round(x['Probability']*c['GridCells']))<1e-8, 'inexact probability grid')
            resolved_color=color if color is not None else OUTCOME_COLORS[(outcome_index+3*o['Palette'])%6]
            outcomes.append(dict(trial_id=key,position=pos,option_id=o['Id'],outcome_id=x['Id'],token_delta=x['TokenDelta'],probability=x['Probability'],practice=r['Practice'],condition=r['ConditionId'],color=resolved_color,configured_color=color,probability_representation=o['ProbabilityRepresentation'],reward_representation=o['RewardRepresentation']))
        check(abs(sum(x['Probability'] for x in xs)-1)<=1e-9, 'probabilities do not sum to one')
        ev=sum(x['Probability']*x['TokenDelta'] for x in xs); var=sum(x['Probability']*(x['TokenDelta']-ev)**2 for x in xs)
        options.append(dict(trial_id=key,position=pos,option_id=o['Id'],ev=ev,variance=var,palette=o['Palette'],chosen=pos==chosen,probability_representation=o['ProbabilityRepresentation'],reward_representation=o['RewardRepresentation']))
    if chosen>=0:
        check(chosen<len(offered), 'invalid chosen index'); o=offered[chosen]; u=r['Draw']
        if not number(u) or not 0<=u<1: raise ValueError('invalid draw')
        check(u==replay_draw(c['Seed'],r['TrialIndex']), 'seed/draw replay mismatch')
        cumulative=0; index=None
        for i,x in enumerate(o['Outcomes']):
            cumulative+=x['Probability']
            if u<cumulative:
                index=i
                break
        if index is None:
            if abs(sum(x['Probability'] for x in o['Outcomes'])-1)>1e-9: raise ValueError('draw exceeds categorical probability mass')
            index=max(i for i,x in enumerate(o['Outcomes']) if x['Probability']>0)
        x=o['Outcomes'][index]
        check(r['OutcomeIndex']==index and r['OutcomeId']==x['Id'] and r['Role']==x['Id'] and amount==x['TokenDelta'] and r['ChosenId']==o['Id'], 'categorical outcome mismatch')
        check(number(r['CommitRTMs']) and c['HoldMs']<=r['CommitRTMs']<=c['DeadlineMs'], 'invalid commit timing')
    else:
        check(chosen==-1 and all(r.get(n) is None for n in ('Tokens','Draw','ChosenId','OutcomeId','CommitRTMs')) and r['OutcomeIndex']==-1, 'uncommitted trial contains outcome')
    before=r['BalanceBefore']; after=r['BalanceAfter']; policy=c['Ledger']; tx=r.get('LedgerTransactionId')
    check(integer(policy['InitialBalance']) and 0<=policy['InitialBalance']<=1000000, 'invalid initial balance')
    check(integer(before) and integer(after), 'balances must be integers')
    check(policy['Scope'] in {'Block','Task'} and policy['InsufficientFunds'] in {'Stop','AllowDebt'} and policy['Settlement']=='VirtualEndowment', 'invalid ledger policy')
    bank_key=r['Practice']; policy_key=json.dumps(policy,sort_keys=True); previous=balances.get(bank_key)
    reset=previous is None or previous['policy']!=policy_key or policy['Scope']=='Block' and previous['block']!=r['BlockIndex']
    expected_before=policy['InitialBalance'] if reset else previous['balance'];check(before==expected_before, 'bank continuity/reset mismatch')
    settled=tx is not None
    check(after==before+(amount if settled else 0), 'ledger arithmetic mismatch')
    check(not settled or tx==key and chosen>=0 and r['RewardStatus']=='VirtualSettled', 'invalid settlement transaction')
    check(settled or r['RewardStatus']=='NotRequested', 'unsettled reward status')
    if policy['InsufficientFunds']=='Stop': check(before>=0 and after>=0, 'implicit debt')
    if r['Status']=='Completed':
        check(chosen>=0 and settled, 'completed without settled choice')
        check(r['RevealActualMs']>=r['OnsetMs']+c['RevealMs'], 'early reveal')
        check(r['TransferEndMs']+.001>=r['RevealActualMs']+c['HighlightMs']+c['TransferMs'], 'short animation window')
        check(r['ItiStartMs']+.001>=r['TransferEndMs']+c['RewardWindowMs'] and r['EndMs']+.001>=r['ItiStartMs']+c['ItiMs'], 'short settlement/ITI window')
    if r['Status'] in {'Omission','Stopped'}: check(chosen==-1 and not settled, 'omission/stop settled')
    can_fund=all(before+x['TokenDelta']>=0 for o in offered for x in o['Outcomes'] if x['Probability']>0)
    check(r['Status']=='Stopped' or policy['InsufficientFunds']=='AllowDebt' or can_fund, 'unfunded offer displayed')
    if r['Status']=='Stopped': check(r.get('Reason')=='InsufficientFunds' and policy['InsufficientFunds']=='Stop' and not can_fund, 'invalid funding stop')
    balances[bank_key]=dict(policy=policy_key,block=r['BlockIndex'],balance=after)
    if tx: ledger_rows.append(dict(trial_id=key,transaction_id=tx,block=r['BlockIndex'],practice=r['Practice'],balance_before=before,token_delta=amount,balance_after=after))
    # Adaptive state changes only after completed nonpractice choices. Bisection is integer resolution.
    a=r['AdaptiveBefore']; z=r.get('AdaptiveAfter'); cfg=c['Adaptive']; adaptive_key=(r['BlockIndex'],json.dumps(cfg,sort_keys=True))
    initial=dict(Enabled=cfg['Enabled'],Lower=cfg['Lower'],Upper=cfg['Upper'],Offer=int((cfg['Lower']+cfg['Upper'])/2),ValidChoices=0,Procedure='integer-bisection-v1')
    previous=adaptive_states.get('last');prior=previous[1] if previous and previous[0]==adaptive_key else initial;check(a==prior, 'adaptive continuity mismatch')
    expected=dict(a)
    if cfg['Enabled']:
        sure=next(o for o in offered if o['Id']==cfg['SureChoiceId']);source=next(o for o in c['Options'] if o['Id']==cfg['SureChoiceId'])['Outcomes'][0]
        check(sure['Outcomes']==[dict(Id=source['Id'],TokenDelta=a['Offer'],Probability=1.0,Color=source.get('Color'))], 'adaptive sure offer mismatch')
        if r['Status']=='Completed' and not r['Practice']:
            if r['ChosenId']==cfg['SureChoiceId']: expected['Upper']=a['Offer']
            elif r['ChosenId']==cfg['RiskyChoiceId']: expected['Lower']=a['Offer']
            expected['ValidChoices']+=1;expected['Offer']=int((expected['Lower']+expected['Upper'])/2)
    if z is not None: check(z==expected, 'invalid adaptive update')
    elif r['Status']=='Completed': check(False,'missing adaptive final snapshot')
    adaptive_states['last']=(adaptive_key,expected)
    if not model:
        check(bool(events), 'missing event stream')
        if events:
            check(all(integer(e['Ordinal']) for e in events) and [e['Ordinal'] for e in events]==list(range(len(events))), 'event ordinal mismatch')
            check(all(number(e['TimeMs']) and r['OnsetMs']<=e['TimeMs']<=r['EndMs'] for e in events), 'event outside interval')
            check(all(a['TimeMs']<=b['TimeMs'] for a,b in zip(events,events[1:])), 'nonmonotonic events')
            types=[e['Type'] for e in events];required=['TrialFinalized']+([] if r['Status']=='Stopped' else ['OfferVisibleSoftware'])
            if chosen>=0: required+=['ChoiceCommitted','OutcomeResolved']
            if r['Status']=='Completed': required+=['RevealVisibleSoftware']
            for t in required: check(types.count(t)==1, 'missing/duplicate '+t)
            check(types.count('LedgerSettled')==int(settled), 'ledger event mismatch')
            if settled: check(next((e['TimeMs'] for e in events if e['Type']=='LedgerSettled'),None)==r['TransferEndMs'], 'settlement time mismatch')
            check(not any(t in types for t in ('RewardRequested','RewardSettled','ZeroReward')), 'virtual trial has pulse event')
            check(types[-1]=='TrialFinalized' and events[-1]['TimeMs']==r['EndMs'], 'final event mismatch')
    trials.append(dict(trial_id=key,trial_index=r['TrialIndex'],status=r['Status'],reason=r.get('Reason'),choice_count=len(offered),chosen_id=r.get('ChosenId'),chosen_position=chosen,role=r.get('Role'),tokens=amount,draw=r.get('Draw'),contact_rt_ms=r.get('FirstContactRTMs'),commit_rt_ms=r.get('CommitRTMs'),requested_pulses=r['RequestedPulses'],observed_pulses=r.get('ObservedPulses'),reward_status=r['RewardStatus'],renderer=c['Renderer'],palette_mode=c['PaletteMode'],iti_ms=c['ItiMs'],reveal_late_ms=r['RevealLateMs'],input_source=input_source,duration_ms=r['EndMs']-r['OnsetMs'],condition=r['ConditionId'],practice=r['Practice'],block=r['BlockIndex'],plan_trial=r['PlanTrialIndex'],balance_before=before,balance_after=after,outcome_id=r.get('OutcomeId'),config_hash=r['ConfigHash']))
