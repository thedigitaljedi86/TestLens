import { filterActiveUsers, sortByName, User } from './user-filter';

const users: User[] = [
  { name: 'Freja', active: true },
  { name: 'Anders', active: false },
  { name: 'Mikkel', active: true },
];

describe('filterActiveUsers', () => {
  it('keeps only active users', () => {
    expect(filterActiveUsers(users).length).toBe(2);
  });

  it('returns an empty list for empty input', () => {
    expect(filterActiveUsers([])).toEqual([]);
  });

  xit('treats missing "active" flag as inactive', () => {
    expect(filterActiveUsers([{ name: 'Ghost' } as User])).toEqual([]);
  });
});

describe('sortByName', () => {
  it('sorts users alphabetically', () => {
    expect(sortByName(users)[0].name).toBe('Anders');
  });

  // it('is stable for equal names', () => {
  //   expect(sortByName([...users, { name: 'Anders', active: true }])[1].active).toBe(true);
  // });
});
