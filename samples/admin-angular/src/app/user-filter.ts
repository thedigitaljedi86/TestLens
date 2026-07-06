export interface User {
  name: string;
  active: boolean;
}

export function filterActiveUsers(users: User[]): User[] {
  return users.filter((u) => u.active);
}

export function sortByName(users: User[]): User[] {
  return [...users].sort((a, b) => a.name.localeCompare(b.name));
}
